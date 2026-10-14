using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wbskt.Auth.Models;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Management.Models;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.E2E.FeatureTests.Scenarios.Outage;

/// <summary>
/// RabbitMQ goes away under running hosts, and comes back.
/// <para>
/// What the code promises while it is gone: a command or a ping is the publish itself, so the
/// management host answers 503 <c>EVENT_BUS_UNAVAILABLE</c> with <c>Retry-After</c> rather than
/// reporting something sent that was not (ClientCommandService, DeviceCommandSender). Sign-in puts its
/// events on the queued bus (QueuedEventBus in the auth host), so it does not wait on the broker. The
/// public webhook callback is sent to the engine's durable queue, and when the broker cannot take it
/// the answer is 503 with <c>Retry-After</c> (PublicCallbackController): nothing is held in the
/// management host, so a delivery made during the outage is not queued to drain later; it is the
/// sender's retry, after the broker is back, that starts the run. That is what is asserted here.
/// </para>
/// <para>
/// The outage is kept short on purpose. Socket hosts report in over the broker, and one silent for
/// <c>Presence:HostStaleAfterSeconds</c> (120 s) has its devices marked offline, after which a
/// command answers 409 instead of reaching the publish.
/// </para>
/// Runs only with <c>E2E_OUTAGE_COMMAND</c> (see <see cref="OutageControl"/>), on its own.
/// </summary>
[Collection(EngineCollection.Name)]
public sealed class BrokerOutageE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task OUT_MQ_01_WithTheBrokerDown_CommandsAnd503_SignInWorks_AndTheHostsRecover()
    {
        Skip.IfNot(E2EConfig.OutageTestsEnabled, OutageControl.SkipReason);
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        // ── Arrange, with the broker up: a connected device and a live webhook trigger ─────────
        var workspace = await fixture.CreateIsolatedWorkspaceAsync();
        var (token, workspaceRef) = workspace;
        var management = fixture.Management.As(token);

        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef);
        var deviceName = $"e2e-mq-outage-{Guid.NewGuid():N}";
        var (clientRef, secret) = await fixture.RegisterClientAsync(pin, deviceName);
        await using var device = new WbsktClient(
            new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, deviceName, null),
            new InMemoryClientStorage(clientRef, secret));
        await device.StartAsync();

        // Presence reaches the management host over the bus, so the device is "connected" there once
        // a command is accepted; only then does a command get as far as the publish.
        await ServicesFixture.PollUntilAsync(
            "a command to the connected device to be accepted",
            async () =>
            {
                using var response = await management.SendClientCommandRawAsync(workspaceRef, clientRef, Command());
                return (response.StatusCode == HttpStatusCode.Accepted, $"HTTP {(int)response.StatusCode}");
            },
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(500));

        var path = $"mq-outage-{Guid.NewGuid():N}";
        var workflowRefId = Guid.NewGuid();
        var definition = new WorkflowBuilder($"E2E-MqOutage-{workflowRefId:N}", workflowRefId)
            .AddWebhookTrigger(path, "POST", WorkflowConcurrencyPolicy.AllowParallel, out _)
            .AddDelay(TimeSpan.FromMilliseconds(10))
            .BuildAndValidate();
        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, definition.Name, JsonSerializer.SerializeToElement(definition, JsonOpts));
        await fixture.FireWebhookUntilRunAsync(token, workspaceRef, publishedRef, path, TimeSpan.FromSeconds(60), new { marker = "before-the-outage" });

        var duringMarker = $"during-{Guid.NewGuid():N}";

        await OutageControl.RunWithRestoreAsync(OutageControl.BrokerStart, async () =>
        {
            await OutageControl.RunAsync(OutageControl.BrokerStop);

            // ── With the broker down ────────────────────────────────────────────────────────────
            using (var command = await management.SendClientCommandRawAsync(workspaceRef, clientRef, Command()))
            {
                await ShouldBeRetryLaterAsync(command, "EVENT_BUS_UNAVAILABLE", "a command is the publish, and the broker cannot take it");
            }

            using (var ping = await management.PingClientRawAsync(workspaceRef, clientRef))
            {
                await ShouldBeRetryLaterAsync(ping, "EVENT_BUS_UNAVAILABLE", "a ping is the publish, and the broker cannot take it");
            }

            using (var login = await fixture.Auth.LoginRawAsync(new LoginRequest(workspace.User.Email, workspace.User.Password)))
            {
                login.StatusCode.Should().Be(HttpStatusCode.OK, "sign-in queues its events instead of waiting on the broker");
            }

            // The issue this pass was written for expected the delivery to be held and drained later;
            // the callback in fact refuses it, so the sender knows to retry (see the class summary).
            using (var webhook = await fixture.Management.SendWebhookRawAsync(workspaceRef, path, new { marker = duringMarker }))
            {
                webhook.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "the callback is queued on the broker, which cannot take it");
                webhook.Headers.RetryAfter.Should().NotBeNull("the sender is told when to try again");
            }

            // ── The broker comes back ───────────────────────────────────────────────────────────
            await OutageControl.RunAsync(OutageControl.BrokerStart);

            // The hosts reconnect on their own; the first publishes after the restart may still fail.
            await ServicesFixture.PollUntilAsync(
                "a ping to be published again after the broker restart",
                async () =>
                {
                    using var response = await management.PingClientRawAsync(workspaceRef, clientRef);
                    return (response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode} {await ServicesFixture.ReadErrorCodeAsync(response)}");
                },
                TimeSpan.FromSeconds(120),
                TimeSpan.FromSeconds(2));

            await ServicesFixture.PollUntilAsync(
                "a command to the device to be accepted again after the broker restart",
                async () =>
                {
                    using var response = await management.SendClientCommandRawAsync(workspaceRef, clientRef, Command());
                    return (response.StatusCode == HttpStatusCode.Accepted, $"HTTP {(int)response.StatusCode} {await ServicesFixture.ReadErrorCodeAsync(response)}");
                },
                TimeSpan.FromSeconds(120),
                TimeSpan.FromSeconds(2));

            // The sender retrying the refused delivery, as Retry-After told it to, starts its run.
            await ServicesFixture.PollUntilAsync(
                "the retried webhook delivery to start a run",
                async () =>
                {
                    using var response = await fixture.Management.SendWebhookRawAsync(workspaceRef, path, new { marker = duringMarker });
                    var carrying = response.IsSuccessStatusCode
                        ? await fixture.ListRunsCarryingAsync(token, workspaceRef, publishedRef, duringMarker)
                        : Array.Empty<Guid>();
                    return (carrying.Count > 0, $"HTTP {(int)response.StatusCode}, {carrying.Count} run(s) carrying the marker");
                },
                TimeSpan.FromSeconds(120),
                TimeSpan.FromSeconds(2));
        });
    }

    private static ClientCommandRequest Command() =>
        new("e2e.outage", "{}", DateTimeOffset.UtcNow.AddMinutes(1));

    private static async Task ShouldBeRetryLaterAsync(HttpResponseMessage response, string code, string because)
    {
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, because);
        response.Headers.RetryAfter.Should().NotBeNull("a 503 here tells the caller when to try again");
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be(code);
    }
}

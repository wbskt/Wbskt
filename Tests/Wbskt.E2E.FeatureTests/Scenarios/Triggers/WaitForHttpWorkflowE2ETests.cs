using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Validation;

namespace Wbskt.E2E.FeatureTests.Scenarios.Triggers;

/// <summary>
/// WaitForHttp round-trip — proves the third bookmark wake path (the public http-wake callback).
///
/// Topology:
///   DeviceTrigger (event="telemetry") ─► WaitForHttp(ttl=15m) ─► action:clientMessage (OpenVent)
///
/// Telemetry starts the run, which parks on an http-wake bookmark keyed "http-wake:{runRefId}".
/// The command must NOT fire yet. An external caller POSTs /api/inbound/wake/{runRefId}, which the
/// engine maps to the same key, resumes the branch, and sends the command.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class WaitForHttpWorkflowE2ETests(ServicesFixture fixture)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task TelemetryParksOnWaitForHttp_Callback_ResumesAndSendsCommand()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspaceRef) = await fixture.LoginAsAdminAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspaceRef, autoApproval: true);

        var deviceName = $"e2e-http-{Guid.NewGuid():N}";
        var (clientRefId, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        var workflowRefId = Guid.NewGuid();
        var definition = BuildHttpWaitDefinition(workflowRefId, clientRefId.ToString());
        new WorkflowValidator().Validate(definition).IsValid.Should().BeTrue();

        var publishedRef = await fixture.PublishWorkflowAsync(
            token, workspaceRef, workflowRefId, $"E2E-Http-{workflowRefId:N}",
            JsonSerializer.SerializeToElement(definition, JsonOpts));

        var storage = new InMemoryClientStorage(clientRefId, secret);
        var clientConfig = new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, deviceName, null);
        var commandTcs = new TaskCompletionSource<(string Action, string? Payload)>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var wbsktClient = new WbsktClient(clientConfig, storage);
        wbsktClient.OnMessageReceived += (action, payload, _) => commandTcs.TrySetResult((action, payload?.ToString()));
        await wbsktClient.StartAsync();

        await wbsktClient.SendAsync("telemetry", new { sensor = "http-test", value = 1 });

        // Wait until the run exists (parked at WaitForHttp); the run RefId is the wake token.
        Guid runRefId = Guid.Empty;
        var runAppeared = await ServicesFixture.PollAsync(
            async () =>
            {
                var runs = await fixture.ListRunsAsync(token, workspaceRef, publishedRef);
                if (runs.Count > 0) { runRefId = runs[0].RefId; return true; }
                return false;
            },
            timeout: TimeSpan.FromSeconds(30), interval: TimeSpan.FromSeconds(1));
        runAppeared.Should().BeTrue("telemetry should have started a run that parks on the http-wait");

        // The gate must block before the callback.
        var leaked = await Task.WhenAny(commandTcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        leaked.Should().NotBe(commandTcs.Task, "the command must not fire before the http-wake callback");

        // External callback resumes the parked branch.
        bool matched = await fixture.SendHttpWakeAsync(runRefId, new { approved = true });
        matched.Should().BeTrue("the wake callback should resume the parked http bookmark");

        var resumed = await Task.WhenAny(commandTcs.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        resumed.Should().Be(commandTcs.Task, "the resumed branch must send the command");
        (await commandTcs.Task).Action.Should().Be("OpenVent");
    }

    private static WorkflowDefinition BuildHttpWaitDefinition(Guid workflowRefId, string clientRef)
    {
        var builder = new WorkflowBuilder($"E2E-Http-{workflowRefId:N}", workflowRefId)
            .AddClientTrigger(clientRef, "telemetry", WorkflowConcurrencyPolicy.AllowParallel, out _)
            .AddWaitForHttp(TimeSpan.FromMinutes(15), timeout => 
            {
                timeout.OnSuccess(b => b.AddClientMessage(clientRef, "OpenVent"));
            });

        return builder.BuildAndValidate();
    }
}

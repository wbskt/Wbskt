using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wbskt;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Clients;

/// <summary>
/// A connected client gets a command at once. An offline, registered one has it queued (202, status
/// "queued") until it connects, when the queue is delivered; a queued command can be listed and
/// cancelled until then.
///
/// Requires the auth, management and socket hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(ClientsCollection.Name)]
public sealed class ClientCommandTests(ServicesFixture fixture)
{
    [SkippableFact]
    public async Task DEV_CMD_01_CommandToAnOfflineClient_IsQueuedAndCanBeCancelled()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var (clientRef, _) = await fixture.RegisterClientAsync(pin, $"e2e-cmd-offline-{Guid.NewGuid():N}");

        var response = await fixture.SendAsync(HttpMethod.Post, Command(workspace, clientRef), token, new { type = "e2e.blink", payload = "{}" });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "the client has never connected, so the command waits for it");
        var sent = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        sent.GetProperty("status").GetString().Should().Be("queued");
        var commandId = sent.GetProperty("commandId").GetGuid();

        var queued = await fixture.SendAsync(HttpMethod.Get, Command(workspace, clientRef) + "?status=queued", token);
        queued.StatusCode.Should().Be(HttpStatusCode.OK);
        var waiting = JsonDocument.Parse(await queued.Content.ReadAsStringAsync()).RootElement;
        waiting.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("commandId").GetGuid()).Should().Equal(commandId);

        var cancel = await fixture.SendAsync(HttpMethod.Delete, $"{Command(workspace, clientRef)}/{commandId}", token);
        cancel.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var again = await fixture.SendAsync(HttpMethod.Delete, $"{Command(workspace, clientRef)}/{commandId}", token);
        again.StatusCode.Should().Be(HttpStatusCode.NotFound, "a cancelled command is no longer waiting");
    }

    [SkippableFact]
    public async Task DEV_CMD_03_CommandQueuedWhileOffline_IsDeliveredOnConnect()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var clientName = $"e2e-cmd-queued-{Guid.NewGuid():N}";
        var (clientRef, secret) = await fixture.RegisterClientAsync(pin, clientName);

        var response = await fixture.SendAsync(HttpMethod.Post, Command(workspace, clientRef), token, new { type = "e2e.queued", payload = "{}" });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var commandId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("commandId").GetString();

        await using var client = new WbsktClient(
            new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, clientName, null),
            new InMemoryClientStorage(clientRef, secret));
        var received = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMessageReceived += (type, _, id) =>
        {
            if (type == "e2e.queued")
            {
                received.TrySetResult(id);
            }
        };
        await client.StartAsync();

        (await received.Task.WaitAsync(TimeSpan.FromSeconds(15))).Should().Be(commandId, "the queue is delivered when the client connects");
    }

    [SkippableFact]
    public async Task DEV_CMD_02_CommandToAConnectedClient_IsDelivered()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var clientName = $"e2e-cmd-online-{Guid.NewGuid():N}";
        var (clientRef, secret) = await fixture.RegisterClientAsync(pin, clientName);

        await using var client = new WbsktClient(
            new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, clientName, null),
            new InMemoryClientStorage(clientRef, secret));
        var received = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.OnMessageReceived += (type, _, commandId) =>
        {
            if (type == "e2e.blink")
            {
                received.TrySetResult(commandId);
            }
        };
        await client.StartAsync();

        var badExpiry = await fixture.SendAsync(HttpMethod.Post, Command(workspace, clientRef), token,
            new { type = "e2e.blink", payload = "{}", expiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
        badExpiry.StatusCode.Should().Be(HttpStatusCode.BadRequest, "an expiry in the past is refused");

        // Presence reaches the management host over the bus, so the first sends may still see the
        // client as offline and queue the command, which the connect then delivers. Either way it arrives.
        HttpStatusCode status = default;
        var accepted = await ServicesFixture.PollAsync(
            async () =>
            {
                var response = await fixture.SendAsync(HttpMethod.Post, Command(workspace, clientRef), token,
                    new { type = "e2e.blink", payload = "{}", expiresAt = DateTimeOffset.UtcNow.AddMinutes(1) });
                status = response.StatusCode;
                return status == HttpStatusCode.Accepted;
            },
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(500));

        accepted.Should().BeTrue($"the client is connected (last answer {status})");
        var commandId = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        commandId.Should().NotBeNullOrEmpty("a command carries its id so the client can ack it");
    }

    private static string Command(Guid workspace, Guid clientRef) =>
        ServicesFixture.ManagementUrl($"/api/workspaces/{workspace}/clients/{clientRef}/commands");
}

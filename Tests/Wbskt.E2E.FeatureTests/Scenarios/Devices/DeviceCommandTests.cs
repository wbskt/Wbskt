using System.Net;
using System.Text.Json;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Devices;

/// <summary>
/// A connected device gets a command at once. An offline, registered one has it queued (202, status
/// "queued") until it connects, when the queue is delivered; a queued command can be listed and
/// cancelled until then.
///
/// Requires the auth, management and socket hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(DevicesCollection.Name)]
public sealed class DeviceCommandTests(ServicesFixture fixture)
{
    [SkippableFact]
    public async Task DEV_CMD_01_CommandToAnOfflineDevice_IsQueuedAndCanBeCancelled()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var (clientRef, _) = await fixture.RegisterClientAsync(pin, $"e2e-cmd-offline-{Guid.NewGuid():N}");

        var response = await fixture.SendAsync(HttpMethod.Post, Command(workspace, clientRef), token, new { type = "e2e.blink", payload = "{}" });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "the device has never connected, so the command waits for it");
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
        var deviceName = $"e2e-cmd-queued-{Guid.NewGuid():N}";
        var (clientRef, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        var response = await fixture.SendAsync(HttpMethod.Post, Command(workspace, clientRef), token, new { type = "e2e.queued", payload = "{}" });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var commandId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("commandId").GetString();

        await using var device = new WbsktClient(
            new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, deviceName, null),
            new InMemoryClientStorage(clientRef, secret));
        var received = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        device.OnMessageReceived += (type, _, id) =>
        {
            if (type == "e2e.queued")
            {
                received.TrySetResult(id);
            }
        };
        await device.StartAsync();

        (await received.Task.WaitAsync(TimeSpan.FromSeconds(15))).Should().Be(commandId, "the queue is delivered when the device connects");
    }

    [SkippableFact]
    public async Task DEV_CMD_02_CommandToAConnectedDevice_IsDelivered()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var deviceName = $"e2e-cmd-online-{Guid.NewGuid():N}";
        var (clientRef, secret) = await fixture.RegisterClientAsync(pin, deviceName);

        await using var device = new WbsktClient(
            new ClientConfig(E2EConfig.ManagementBaseUrl, E2EConfig.SocketWsBaseUrl, deviceName, null),
            new InMemoryClientStorage(clientRef, secret));
        var received = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        device.OnMessageReceived += (type, _, commandId) =>
        {
            if (type == "e2e.blink")
            {
                received.TrySetResult(commandId);
            }
        };
        await device.StartAsync();

        var badExpiry = await fixture.SendAsync(HttpMethod.Post, Command(workspace, clientRef), token,
            new { type = "e2e.blink", payload = "{}", expiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
        badExpiry.StatusCode.Should().Be(HttpStatusCode.BadRequest, "an expiry in the past is refused");

        // Presence reaches the management host over the bus, so the first sends may still see the
        // device as offline and queue the command, which the connect then delivers. Either way it arrives.
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

        accepted.Should().BeTrue($"the device is connected (last answer {status})");
        var commandId = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        commandId.Should().NotBeNullOrEmpty("a command carries its id so the device can ack it");
    }

    private static string Command(Guid workspace, Guid clientRef) =>
        ServicesFixture.ManagementUrl($"/api/workspaces/{workspace}/clients/{clientRef}/commands");
}

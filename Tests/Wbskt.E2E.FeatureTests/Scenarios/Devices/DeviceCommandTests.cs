using System.Net;
using FluentAssertions;
using Wbskt.Client.Sdk;
using Wbskt.Client.Sdk.Models;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Devices;

/// <summary>
/// Commands are delivered live or not at all. Sending one to an offline device is refused up front
/// (409 DEVICE_OFFLINE) instead of being accepted and silently dropped; a connected device gets it.
///
/// Requires the auth, management and socket hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(E2ECollection.Name)]
public sealed class DeviceCommandTests(ServicesFixture fixture)
{
    [SkippableFact]
    public async Task DEV_CMD_01_CommandToAnOfflineDevice_IsRefused()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.RegisterAndLoginUserAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var (clientRef, _) = await fixture.RegisterClientAsync(pin, $"e2e-cmd-offline-{Guid.NewGuid():N}");

        var response = await fixture.SendAsync(HttpMethod.Post, Command(workspace, clientRef), token, new { type = "e2e.blink", payload = "{}" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "the device has never connected");
        (await ServicesFixture.ReadErrorCodeAsync(response)).Should().Be("DEVICE_OFFLINE");
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
        // device as offline.
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

using System.Net;
using System.Net.WebSockets;
using FluentAssertions;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Management.Models;
using Wbskt.Protocol;

namespace Wbskt.E2E.FeatureTests.Scenarios.Devices;

/// <summary>
/// The socket host against a device that is not the SDK (<see cref="RawDeviceSocket"/>): what it does
/// with a frame that is not a message, a message over the size limit, a device over its message
/// budget, a token minted before the device was revoked, a subprotocol it does not speak, and its
/// plain HTTP surface.
///
/// Requires the auth, management and socket hosts running. Skips gracefully when they are down.
/// </summary>
[Collection(DevicesCollection.Name)]
public sealed class RawProtocolTests(ServicesFixture fixture)
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(30);

    // Socket:MessageBudget as the socket host defaults it (MessageBudgetOptions); start-hosts.sh and
    // CI leave it alone. Burst + CloseAfterDropped messages sent faster than the refill closes the
    // connection; the flood below sends several times that, and stops as soon as the Close arrives.
    private const int BudgetBurst = 100;
    private const int BudgetCloseAfterDropped = 500;
    private const int FloodCeiling = 10 * (BudgetBurst + BudgetCloseAfterDropped);

    [SkippableFact]
    public async Task RAW_01_AMalformedFrame_IsDropped_AndNeitherThatDeviceNorAnotherIsDisconnected()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var (sender, senderToken) = await DeviceAsync(pin, "raw-malformed");
        var (bystander, bystanderToken) = await DeviceAsync(pin, "raw-bystander");

        await using var a = await RawDeviceSocket.ConnectAsync(senderToken);
        await using var b = await RawDeviceSocket.ConnectAsync(bystanderToken);

        // Not JSON, then JSON with no type: both are dropped without an answer (InboundMessageDispatcher).
        await a.SendRawAsync("{this is not json");
        await a.SendRawAsync("{\"payload\":{}}");

        // Both connections still carry messages afterwards.
        string senderType = $"e2e.raw.{Guid.NewGuid():N}";
        string bystanderType = $"e2e.raw.{Guid.NewGuid():N}";
        await a.SendAsync(new SocketMessage(senderType, new { n = 1 }));
        await b.SendAsync(new SocketMessage(bystanderType, new { n = 2 }));

        await WaitForInboundAsync(token, workspace, sender, senderType);
        await WaitForInboundAsync(token, workspace, bystander, bystanderType);
        a.State.Should().Be(WebSocketState.Open, "a malformed frame is dropped, not punished");
        b.State.Should().Be(WebSocketState.Open);
    }

    [SkippableFact]
    public async Task RAW_02_AMessageOverTheSizeLimit_ClosesWithMessageTooBig()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var (_, deviceToken) = await DeviceAsync(pin, "raw-too-big");

        await using var socket = await RawDeviceSocket.ConnectAsync(deviceToken);
        var closed = socket.ReceiveUntilClosedAsync(CloseTimeout);

        // One byte over: the host has read the whole message by the time it knows, so nothing is
        // left unread on the connection when it closes.
        await socket.SendRawAsync(new string('x', SocketProtocol.MaxMessageBytes + 1));

        (await closed).Should().Be(WebSocketCloseStatus.MessageTooBig);
        socket.CloseStatusDescription.Should().Contain(SocketProtocol.MaxMessageBytes.ToString());
    }

    [SkippableFact]
    public async Task RAW_03_ADeviceOverItsMessageBudget_IsClosedForPolicyViolation()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var (_, deviceToken) = await DeviceAsync(pin, "raw-flood");

        await using var socket = await RawDeviceSocket.ConnectAsync(deviceToken);
        var closed = socket.ReceiveUntilClosedAsync(CloseTimeout);

        // An unknown sys.* type: the host ignores it after the budget check, so the messages let
        // through cost nothing and the flood stays faster than the refill.
        const string noise = "{\"type\":\"sys.e2e.noise\",\"payload\":null}";
        int sent = 0;
        try
        {
            while (!closed.IsCompleted && sent < FloodCeiling)
            {
                await socket.SendRawAsync(noise);
                sent++;
            }
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The host has gone away mid-flood; its Close frame is what is under test.
        }

        (await closed).Should().Be(
            WebSocketCloseStatus.PolicyViolation,
            $"{sent} messages in a tight loop exceed a budget of {BudgetBurst} plus {BudgetCloseAfterDropped} dropped in a row");
        socket.CloseStatusDescription.Should().Be("Too many messages.");
        sent.Should().BeGreaterThanOrEqualTo(BudgetBurst + BudgetCloseAfterDropped);
    }

    [SkippableFact]
    public async Task RAW_04_AStaleToken_IsRefusedAtConnect()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var (clientRef, deviceToken) = await DeviceAsync(pin, "raw-stale");

        var (before, socket) = await RawDeviceSocket.TryConnectAsync(deviceToken);
        before.Should().Be(HttpStatusCode.SwitchingProtocols);
        await socket!.DisposeAsync();

        await fixture.Management.As(token).UpdateClientAsync(workspace, clientRef, new UpdateClientRequest(null, ClientStatus.Revoked));

        // The token itself is still well formed and unexpired: the host has to know it was revoked.
        // It hears of the revocation from the management host, so allow it a moment.
        await ServicesFixture.PollUntilAsync(
            $"the socket host to refuse revoked client {clientRef}",
            async () =>
            {
                var (status, refused) = await RawDeviceSocket.TryConnectAsync(deviceToken);
                if (refused is not null)
                {
                    await refused.DisposeAsync();
                }

                return (status == HttpStatusCode.Forbidden, $"HTTP {(int)status}");
            },
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(500));

        // A token whose signature does not verify never gets that far.
        string tampered = deviceToken[..^4] + (deviceToken.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");
        (await RawDeviceSocket.TryConnectAsync(tampered)).Status.Should().Be(HttpStatusCode.Unauthorized);
        (await RawDeviceSocket.TryConnectAsync(null)).Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task RAW_05_TheSubprotocol_IsV1OrNone()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        var (token, workspace) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var (_, deviceToken) = await DeviceAsync(pin, "raw-subprotocol");

        await using (var v1 = await RawDeviceSocket.ConnectAsync(deviceToken))
        {
            v1.SubProtocol.Should().Be(SocketProtocol.V1);
        }

        await using (var none = await RawDeviceSocket.ConnectAsync(deviceToken, subProtocol: null))
        {
            none.SubProtocol.Should().BeNull("a client that offers nothing is spoken to as v1 without one");
        }

        (await RawDeviceSocket.TryConnectAsync(deviceToken, "wbskt.v999")).Status.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task RAW_06_TheSocketHostsHttpSurface()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");

        (await fixture.SendAsync(HttpMethod.Get, $"{E2EConfig.SocketHttpBaseUrl}/healthz")).StatusCode
            .Should().Be(HttpStatusCode.OK);

        string ws = $"{E2EConfig.SocketHttpBaseUrl}/ws";
        (await fixture.SendAsync(HttpMethod.Get, ws)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "/ws takes a device token");

        var (token, workspace) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var (_, deviceToken) = await DeviceAsync(pin, "raw-http");

        using var plain = await fixture.SendAsync(HttpMethod.Get, ws, deviceToken);
        plain.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ServicesFixture.ReadErrorCodeAsync(plain)).Should().Be("SOCKET_UPGRADE_REQUIRED");
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────

    private async Task<(Guid ClientRef, string Token)> DeviceAsync(string pin, string name)
    {
        var (clientRef, secret) = await fixture.RegisterClientAsync(pin, $"{name}-{Guid.NewGuid():N}");
        return (clientRef, await fixture.LoginClientAsync(clientRef, secret));
    }

    /// <summary>Waits for a device's message of <paramref name="messageType"/> to reach its comms feed.</summary>
    private Task WaitForInboundAsync(string token, Guid workspace, Guid clientRef, string messageType) =>
        ServicesFixture.PollUntilAsync(
            $"a '{messageType}' message from client {clientRef} in its comms",
            async () =>
            {
                var comms = await fixture.Management.As(token).GetClientCommsAsync(workspace, clientRef, direction: "in");
                return (comms.Items.Any(e => e.EventData.Contains(messageType, StringComparison.Ordinal)), $"{comms.Items.Count()} inbound entries");
            },
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(1));
}

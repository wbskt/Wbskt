using System.Net.WebSockets;
using System.Text.Json;
using FluentAssertions;
using MQTTnet;
using Wbskt.E2E.FeatureTests.Fixtures;
using Wbskt.Management.Models;
using Wbskt.Protocol;

namespace Wbskt.E2E.FeatureTests.Scenarios.Clients;

/// <summary>
/// The MQTT gateway with the rest of the platform (Docs/Client.Protocol.MQTT.md): a client's messages
/// reach its comms, a command reaches its down topic and its ack comes back, a revoked client is
/// closed and then refused, a client moving between the MQTT and WebSocket gateways supersedes its
/// older connection, and a close for a firmware update shows the client as updating. The gateway's
/// own rules (topics, packet size, reason codes) are covered by its unit tests over loopback.
///
/// Requires the auth, management, WebSocket and MQTT gateway hosts running. Skips gracefully when
/// they are down.
/// </summary>
[Collection(ClientsCollection.Name)]
public sealed class MqttGatewayTests(ServicesFixture fixture)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [SkippableFact]
    public async Task MQTT_01_AClientsMessages_ReachItsComms_AndItShowsConnected()
    {
        SkipUnlessHostsAreUp();

        var (token, workspace) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var (clientRef, clientToken) = await ClientAsync(pin, "mqtt-message");

        await using var client = await RawMqttClient.ConnectAsync(clientRef, clientToken);
        string type = $"e2e.mqtt.{Guid.NewGuid():N}";
        await client.PublishAsync(new ProtocolMessage(type, new { c = 21 }));

        await WaitForConnectedAsync(token, workspace, clientRef);
        await ServicesFixture.PollUntilAsync(
            $"a '{type}' message from client {clientRef} in its comms",
            async () =>
            {
                var comms = await fixture.Management.As(token).GetClientCommsAsync(workspace, clientRef, direction: "in");
                return (comms.Items.Any(e => e.EventData.Contains(type, StringComparison.Ordinal)), $"{comms.Items.Count()} inbound entries");
            },
            Patience,
            TimeSpan.FromSeconds(1));
    }

    [SkippableFact]
    public async Task MQTT_02_ACommand_ArrivesOnTheDownTopic_AndItsAckIsRecorded()
    {
        SkipUnlessHostsAreUp();

        var (token, workspace) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var (clientRef, clientToken) = await ClientAsync(pin, "mqtt-command");

        await using var client = await RawMqttClient.ConnectAsync(clientRef, clientToken);
        await WaitForConnectedAsync(token, workspace, clientRef);

        // Sent or queued, it reaches the down topic: a queued command is delivered as the client connects.
        var response = await fixture.SendAsync(HttpMethod.Post, ServicesFixture.ManagementUrl($"/api/v1/workspaces/{workspace}/clients/{clientRef}/commands"), token,
            new { type = "e2e.blink", payload = "{\"times\":2}" });
        response.EnsureSuccessStatusCode();
        string commandId = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("commandId").GetString()!;

        var command = await client.NextOfAsync("e2e.blink", Patience);
        command.GetProperty("commandId").GetString().Should().Be(commandId);

        await client.PublishAsync(new ProtocolMessage(MessageTypes.Ack, new AckPayload(commandId)));

        await ServicesFixture.PollUntilAsync(
            $"the ack of command {commandId} in the event log",
            async () =>
            {
                var acks = (await fixture.GetEventLogsAsync(token, workspace, "ClientCommandAckedEvent")).Where(l => l.ClientRefId == clientRef).ToList();
                return (acks.Any(l => l.EventData.Contains(commandId, StringComparison.OrdinalIgnoreCase)), $"{acks.Count} acks");
            },
            Patience,
            TimeSpan.FromSeconds(1));
    }

    [SkippableFact]
    public async Task MQTT_03_ARevokedClient_IsDisconnectedAsNotAuthorized_AndRefusedAtConnect()
    {
        SkipUnlessHostsAreUp();

        var (token, workspace) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var (clientRef, clientToken) = await ClientAsync(pin, "mqtt-revoked");

        await using var client = await RawMqttClient.ConnectAsync(clientRef, clientToken);
        await WaitForConnectedAsync(token, workspace, clientRef);

        await fixture.Management.As(token).UpdateClientAsync(workspace, clientRef, new UpdateClientRequest(null, ClientStatus.Revoked));

        (await client.Disconnected.WaitAsync(Patience)).Should().Be(MqttClientDisconnectReason.NotAuthorized);

        // The token is still well formed and unexpired: the gateway has to know it was revoked.
        await ServicesFixture.PollUntilAsync(
            $"the MQTT gateway to refuse revoked client {clientRef}",
            async () =>
            {
                var (code, accepted) = await RawMqttClient.TryConnectAsync(clientRef, clientToken);
                if (accepted is not null)
                {
                    await accepted.DisposeAsync();
                }

                return (code == MqttClientConnectResultCode.NotAuthorized, code.ToString());
            },
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(500));

        // A token whose signature does not verify is a bad password, which tells the client to log in again.
        string tampered = clientToken[..^4] + (clientToken.EndsWith("AAAA", StringComparison.Ordinal) ? "BBBB" : "AAAA");
        (await RawMqttClient.TryConnectAsync(clientRef, tampered)).Code.Should().Be(MqttClientConnectResultCode.BadUserNameOrPassword);
    }

    [SkippableFact]
    public async Task MQTT_04_AClientMovingBetweenGateways_SupersedesItsOlderConnection()
    {
        SkipUnlessHostsAreUp();

        var (token, workspace) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var (clientRef, clientToken) = await ClientAsync(pin, "mqtt-moving");

        // WebSocket first, then MQTT: the WebSocket gateway hears of the new connection over the bus.
        await using var socket = await RawClientSocket.ConnectAsync(clientToken);
        var socketClosed = socket.ReceiveUntilClosedAsync(Patience);
        await WaitForConnectedAsync(token, workspace, clientRef);

        var mqtt = await RawMqttClient.ConnectAsync(clientRef, clientToken);
        await using (mqtt)
        {
            (await socketClosed).Should().Be((WebSocketCloseStatus)WebSocketBinding.SupersededCloseCode);

            // And back: the MQTT connection is the older one now.
            await using var again = await RawClientSocket.ConnectAsync(clientToken);
            (await mqtt.Disconnected.WaitAsync(Patience)).Should().Be(MqttClientDisconnectReason.SessionTakenOver);
            again.State.Should().Be(WebSocketState.Open);

            // A superseded connection is not the client going offline.
            await WaitForConnectedAsync(token, workspace, clientRef);
        }
    }

    [SkippableFact]
    public async Task MQTT_05_AClientClosingForAnUpdate_ShowsAsUpdating()
    {
        SkipUnlessHostsAreUp();

        var (token, workspace) = await fixture.CreateIsolatedWorkspaceAsync();
        var (_, pin) = await fixture.CreatePolicyAsync(token, workspace, autoApproval: true);
        var (clientRef, clientToken) = await ClientAsync(pin, "mqtt-updating");

        await using var client = await RawMqttClient.ConnectAsync(clientRef, clientToken);
        await WaitForConnectedAsync(token, workspace, clientRef);

        await client.CloseForUpdateAsync();

        await ServicesFixture.PollUntilAsync(
            $"client {clientRef} to show as updating",
            async () =>
            {
                var detail = await fixture.Management.As(token).GetClientAsync(workspace, clientRef);
                return (!detail.IsConnected && detail.UpdatingUntil > DateTimeOffset.UtcNow, $"connected {detail.IsConnected}, updating until {detail.UpdatingUntil}");
            },
            Patience,
            TimeSpan.FromSeconds(1));
    }

    private void SkipUnlessHostsAreUp()
    {
        Skip.IfNot(fixture.HostsAvailable, "E2E hosts not running — skipping.");
        Skip.IfNot(fixture.MqttGatewayAvailable, $"No MQTT gateway at {E2EConfig.MqttUrl} — skipping.");
    }

    private async Task<(Guid ClientRef, string Token)> ClientAsync(string pin, string name)
    {
        var (clientRef, secret) = await fixture.RegisterClientAsync(pin, $"{name}-{Guid.NewGuid():N}");
        return (clientRef, await fixture.LoginClientAsync(clientRef, secret));
    }

    private Task WaitForConnectedAsync(string token, Guid workspace, Guid clientRef) =>
        ServicesFixture.PollUntilAsync(
            $"client {clientRef} to show connected",
            async () =>
            {
                var detail = await fixture.Management.As(token).GetClientAsync(workspace, clientRef);
                return (detail.IsConnected, $"connected {detail.IsConnected}");
            },
            Patience,
            TimeSpan.FromMilliseconds(500));
}

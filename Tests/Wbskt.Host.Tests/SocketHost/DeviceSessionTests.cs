using System.Net.WebSockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Events.Client;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Gateway.Core.Services;
using Wbskt.Gateway.Core.Services.Inbound;
using Wbskt.Gateway.Core.Telemetry;
using Wbskt.Socket.Host.Infrastructure;

namespace Wbskt.Host.Tests.SocketHost;

/// <summary>
/// The session steps the socket host's receive loop used to do inline, now in Gateway.Core: what a
/// device sees (its close code) and what the rest of the system hears (connected, disconnected).
/// </summary>
public sealed class DeviceSessionTests
{
    private const string ThisHost = "gateway-a";
    private static readonly Guid ClientRefId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private readonly Mock<IEventBus> _bus = new();
    private readonly ConnectionManager _connections = new(NullLogger<ConnectionManager>.Instance);

    private DeviceSessions Sessions(MessageBudgetOptions? budget = null)
    {
        var metrics = new GatewayMetrics(GatewayTransport.WebSocket);
        var dispatcher = new InboundMessageDispatcher([], new DeviceMessageHandler(_bus.Object), metrics, NullLogger<InboundMessageDispatcher>.Instance);
        return new DeviceSessions(_connections, _bus.Object, new BusInstanceId(ThisHost), metrics, dispatcher,
            Options.Create(budget ?? new MessageBudgetOptions()), NullLogger<DeviceSessions>.Instance, NullLogger<DeviceInbox>.Instance);
    }

    private static (ClientConnection Connection, Mock<WebSocket> Socket) Connection()
    {
        var socket = new Mock<WebSocket>();
        socket.SetupGet(x => x.State).Returns(WebSocketState.Open);
        socket.Setup(x => x.CloseOutputAsync(It.IsAny<WebSocketCloseStatus>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return (new ClientConnection { Socket = socket.Object, ClientId = 42, WorkspaceId = 7 }, socket);
    }

    [Fact]
    public async Task A_session_announces_the_device_connected_and_disconnected_from_this_host()
    {
        var sessions = Sessions();
        var (connection, _) = Connection();

        sessions.Open(ClientRefId, connection);
        await sessions.AnnounceConnectedAsync(ClientRefId, connection);
        Assert.Same(connection, _connections.GetConnection(ClientRefId));

        sessions.Release(ClientRefId, connection);
        await sessions.AnnounceEndedAsync(ClientRefId, connection, DisconnectReason.Aborted);

        Assert.Null(_connections.GetConnection(ClientRefId));
        _bus.Verify(x => x.PublishAsync(It.Is<ClientConnectedEvent>(e => e.ClientRefId == ClientRefId && e.ClientId == 42 && e.WorkspaceId == 7 && e.HostId == ThisHost), CancellationToken.None), Times.Once);
        _bus.Verify(x => x.PublishAsync(It.Is<ClientDisconnectedEvent>(e => e.ClientRefId == ClientRefId && e.Reason == DisconnectReason.Aborted.Describe() && e.HostId == ThisHost), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task A_requested_close_names_its_own_reason_in_the_disconnect()
    {
        var sessions = Sessions();
        var (connection, _) = Connection();
        sessions.Open(ClientRefId, connection);
        await connection.RequestCloseAsync(DisconnectReason.Revoked, "Client deleted.");

        await sessions.AnnounceEndedAsync(ClientRefId, connection, DisconnectReason.Revoked);

        _bus.Verify(x => x.PublishAsync(It.Is<ClientDisconnectedEvent>(e => e.Reason == "Client deleted."), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task A_superseded_session_does_not_announce_the_device_offline()
    {
        var sessions = Sessions();
        var (old, oldSocket) = Connection();
        var (fresh, _) = Connection();
        sessions.Open(ClientRefId, old);
        sessions.Open(ClientRefId, fresh);

        sessions.Release(ClientRefId, old);
        await sessions.AnnounceEndedAsync(ClientRefId, old, old.CloseRequest!.Reason);

        oldSocket.Verify(x => x.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, ConnectionManager.SupersededDescription, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Same(fresh, _connections.GetConnection(ClientRefId));
        _bus.Verify(x => x.PublishAsync(It.IsAny<ClientDisconnectedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_device_that_stays_over_its_budget_is_closed_with_1008()
    {
        var (connection, socket) = Connection();
        var inbox = Sessions(new MessageBudgetOptions { PerSecond = 0.1, Burst = 1, CloseAfterDropped = 2 }).CreateInbox(ClientRefId, connection);

        Assert.Null(await inbox.ReceiveAsync("""{"type":"t","payload":{}}""", CancellationToken.None));
        Assert.Null(await inbox.ReceiveAsync("""{"type":"t","payload":{}}""", CancellationToken.None));
        Assert.Equal(DisconnectReason.Throttled, await inbox.ReceiveAsync("""{"type":"t","payload":{}}""", CancellationToken.None));

        socket.Verify(x => x.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "Too many messages.", It.IsAny<CancellationToken>()), Times.Once);
        _bus.Verify(x => x.PublishAsync(It.IsAny<ClientMessageReceivedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Messages_after_a_requested_close_are_ignored()
    {
        var (connection, _) = Connection();
        var inbox = Sessions().CreateInbox(ClientRefId, connection);
        await connection.RequestCloseAsync(DisconnectReason.Revoked, "x");

        Assert.Null(await inbox.ReceiveAsync("""{"type":"t","payload":{}}""", CancellationToken.None));

        _bus.Verify(x => x.PublishAsync(It.IsAny<ClientMessageReceivedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

/// <summary>The close codes are part of the device protocol: moving the reasons into Gateway.Core must not change them.</summary>
public sealed class WebSocketCloseStatusesTests
{
    [Theory]
    [InlineData(DisconnectReason.Revoked, WebSocketCloseStatus.PolicyViolation)]
    [InlineData(DisconnectReason.Superseded, WebSocketCloseStatus.PolicyViolation)]
    [InlineData(DisconnectReason.Throttled, WebSocketCloseStatus.PolicyViolation)]
    [InlineData(DisconnectReason.MessageTooBig, WebSocketCloseStatus.MessageTooBig)]
    [InlineData(DisconnectReason.ServerStopping, WebSocketCloseStatus.EndpointUnavailable)]
    [InlineData(DisconnectReason.ClientClosed, WebSocketCloseStatus.NormalClosure)]
    public void Each_reason_keeps_its_close_code(DisconnectReason reason, WebSocketCloseStatus expected)
    {
        Assert.Equal(expected, WebSocketCloseStatuses.For(reason));
    }
}

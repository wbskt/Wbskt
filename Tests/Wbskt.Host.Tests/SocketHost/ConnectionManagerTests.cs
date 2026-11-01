using System.Net.WebSockets;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Socket.Host.Infrastructure;

namespace Wbskt.Host.Tests.SocketHost;

public sealed class ConnectionManagerTests
{
    private static readonly Guid ClientRefId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Fact]
    public void A_reconnect_on_the_same_host_wins_and_the_old_connection_is_closed_as_superseded()
    {
        var manager = new ConnectionManager(NullLogger<ConnectionManager>.Instance);
        var (old, oldSocket) = Connection();
        var (fresh, _) = Connection();

        manager.Register(ClientRefId, old);
        manager.Register(ClientRefId, fresh);

        Assert.Same(fresh, manager.GetConnection(ClientRefId));
        Assert.Equal(DisconnectReason.Superseded, old.CloseRequest?.Reason);
        oldSocket.Verify(x => x.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Null(fresh.CloseRequest);
    }

    [Fact]
    public void The_old_connections_cleanup_does_not_remove_its_replacement()
    {
        var manager = new ConnectionManager(NullLogger<ConnectionManager>.Instance);
        var (old, _) = Connection();
        var (fresh, _) = Connection();
        manager.Register(ClientRefId, old);
        manager.Register(ClientRefId, fresh);

        Assert.False(manager.Unregister(ClientRefId, old));
        Assert.Same(fresh, manager.GetConnection(ClientRefId));
        Assert.True(manager.Unregister(ClientRefId, fresh));
        Assert.Null(manager.GetConnection(ClientRefId));
    }

    [Fact]
    public async Task A_close_is_sent_once_however_often_it_is_asked_for()
    {
        var manager = new ConnectionManager(NullLogger<ConnectionManager>.Instance);
        var (connection, socket) = Connection();
        manager.Register(ClientRefId, connection);

        Assert.True(await manager.RequestCloseAsync(ClientRefId, DisconnectReason.Revoked, "Client deleted."));
        await manager.RequestCloseAsync(ClientRefId, DisconnectReason.Superseded, "again");

        Assert.Equal(new CloseRequest(DisconnectReason.Revoked, "Client deleted."), connection.CloseRequest);
        socket.Verify(x => x.CloseOutputAsync(It.IsAny<WebSocketCloseStatus>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        socket.Verify(x => x.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "Client deleted.", It.IsAny<CancellationToken>()), Times.Once);
        // The connection stays registered: only its receive loop unregisters it.
        Assert.Same(connection, manager.GetConnection(ClientRefId));
    }

    [Fact]
    public async Task A_close_that_cannot_be_sent_cuts_the_connection_at_once()
    {
        var (connection, socket) = Connection();
        socket.Setup(x => x.CloseOutputAsync(It.IsAny<WebSocketCloseStatus>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WebSocketException("gone"));

        await connection.RequestCloseAsync(DisconnectReason.Revoked, "x");

        Assert.True(connection.Closing.IsCancellationRequested);
    }

    [Fact]
    public async Task Asking_a_host_without_the_client_to_close_it_is_a_no_op()
    {
        var manager = new ConnectionManager(NullLogger<ConnectionManager>.Instance);

        Assert.False(await manager.RequestCloseAsync(ClientRefId, DisconnectReason.Revoked, "x"));
    }

    private static (ClientConnection Connection, Mock<WebSocket> Socket) Connection()
    {
        var socket = new Mock<WebSocket>();
        socket.SetupGet(x => x.State).Returns(WebSocketState.Open);
        socket.Setup(x => x.CloseOutputAsync(It.IsAny<WebSocketCloseStatus>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return (new ClientConnection { Socket = socket.Object, ClientId = 1, WorkspaceId = 2 }, socket);
    }
}

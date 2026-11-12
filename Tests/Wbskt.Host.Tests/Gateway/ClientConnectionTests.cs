using System.Net.WebSockets;
using Moq;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Gateway.WebSocket.Host.Infrastructure;

namespace Wbskt.Host.Tests.Gateway;

/// <summary>The WebSocket host's connection: what it does with the socket. The core is tested through <see cref="FakeClientConnection"/>.</summary>
public sealed class ClientConnectionTests
{
    [Fact]
    public async Task A_requested_close_is_sent_with_its_reasons_close_code()
    {
        var (connection, socket) = Connection();

        await connection.RequestCloseAsync(DisconnectReason.Revoked, "Client deleted.");

        socket.Verify(x => x.CloseOutputAsync((WebSocketCloseStatus)4002, "Client deleted.", It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task The_sessions_final_close_is_sent_with_its_reasons_close_code()
    {
        var (connection, socket) = Connection();

        await connection.CloseTransportAsync(DisconnectReason.ServerStopping);

        socket.Verify(x => x.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, DisconnectReason.ServerStopping.Describe(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_final_close_the_socket_refuses_does_not_throw()
    {
        var (connection, socket) = Connection();
        socket.Setup(x => x.CloseOutputAsync(It.IsAny<WebSocketCloseStatus>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WebSocketException("gone"));

        await connection.CloseTransportAsync(DisconnectReason.ClientClosed);
    }

    private static (ClientConnection Connection, Mock<System.Net.WebSockets.WebSocket> Socket) Connection()
    {
        var socket = new Mock<System.Net.WebSockets.WebSocket>();
        socket.SetupGet(x => x.State).Returns(WebSocketState.Open);
        socket.Setup(x => x.CloseOutputAsync(It.IsAny<WebSocketCloseStatus>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return (new ClientConnection { Socket = socket.Object, ClientId = 1, WorkspaceId = 2, TokenIssuedAtUtc = DateTime.UtcNow }, socket);
    }
}

/// <summary>The close codes are part of the client protocol: moving the reasons into Gateway.Core must not change them.</summary>
public sealed class WebSocketCloseStatusesTests
{
    [Theory]
    [InlineData(DisconnectReason.Revoked, (WebSocketCloseStatus)4002)]
    [InlineData(DisconnectReason.Superseded, (WebSocketCloseStatus)4001)]
    [InlineData(DisconnectReason.Throttled, (WebSocketCloseStatus)4003)]
    [InlineData(DisconnectReason.MessageTooBig, WebSocketCloseStatus.MessageTooBig)]
    [InlineData(DisconnectReason.ServerStopping, WebSocketCloseStatus.EndpointUnavailable)]
    [InlineData(DisconnectReason.Error, WebSocketCloseStatus.InternalServerError)]
    [InlineData(DisconnectReason.ClientClosed, WebSocketCloseStatus.NormalClosure)]
    public void Each_reason_keeps_its_close_code(DisconnectReason reason, WebSocketCloseStatus expected)
    {
        Assert.Equal(expected, WebSocketCloseStatuses.For(reason));
    }
}

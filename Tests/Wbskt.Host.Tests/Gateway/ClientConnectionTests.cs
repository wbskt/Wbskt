using System.Net.WebSockets;
using Moq;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Gateway.WebSocket.Host.Infrastructure;

namespace Wbskt.Host.Tests.Gateway;

/// <summary>The WebSocket host's connection: what it does with the socket. The core is tested through <see cref="FakeDeviceConnection"/>.</summary>
public sealed class ClientConnectionTests
{
    [Fact]
    public async Task A_requested_close_is_sent_with_its_reasons_close_code()
    {
        var (connection, socket) = Connection();

        await connection.RequestCloseAsync(DisconnectReason.Revoked, "Client deleted.");

        socket.Verify(x => x.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "Client deleted.", It.IsAny<CancellationToken>()), Times.Once);
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

    private static (ClientConnection Connection, Mock<System.Net.WebSockets.WebSocket> Socket) Connection()
    {
        var socket = new Mock<System.Net.WebSockets.WebSocket>();
        socket.SetupGet(x => x.State).Returns(WebSocketState.Open);
        socket.Setup(x => x.CloseOutputAsync(It.IsAny<WebSocketCloseStatus>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return (new ClientConnection { Socket = socket.Object, ClientId = 1, WorkspaceId = 2 }, socket);
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
    [InlineData(DisconnectReason.Error, WebSocketCloseStatus.InternalServerError)]
    [InlineData(DisconnectReason.ClientClosed, WebSocketCloseStatus.NormalClosure)]
    public void Each_reason_keeps_its_close_code(DisconnectReason reason, WebSocketCloseStatus expected)
    {
        Assert.Equal(expected, WebSocketCloseStatuses.For(reason));
    }
}

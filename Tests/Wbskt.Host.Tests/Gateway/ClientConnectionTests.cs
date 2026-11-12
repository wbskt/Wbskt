using System.Net;
using System.Net.Sockets;
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

    [Fact]
    public async Task A_stopping_host_sends_the_client_a_1001_close_it_can_read()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var clientTcp = new TcpClient();
        await clientTcp.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var serverTcp = await listener.AcceptTcpClientAsync();
        using var serverSocket = System.Net.WebSockets.WebSocket.CreateFromStream(serverTcp.GetStream(), isServer: true, subProtocol: null, keepAliveInterval: Timeout.InfiniteTimeSpan);
        using var clientSocket = System.Net.WebSockets.WebSocket.CreateFromStream(clientTcp.GetStream(), isServer: false, subProtocol: null, keepAliveInterval: Timeout.InfiniteTimeSpan);
        using var connection = new ClientConnection { Socket = serverSocket, ClientId = 1, WorkspaceId = 2 };
        using var stopping = new CancellationTokenSource();
        using var closeOnStop = connection.CloseOnStop(stopping.Token);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await stopping.CancelAsync();
        var received = await clientSocket.ReceiveAsync(new byte[64], timeout.Token);

        Assert.Equal(WebSocketMessageType.Close, received.MessageType);
        Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, clientSocket.CloseStatus);
        Assert.Equal(DisconnectReason.ServerStopping, connection.CloseRequest?.Reason);
    }

    [Fact]
    public async Task A_connection_that_ended_before_the_host_stopped_is_not_closed_again()
    {
        var (connection, socket) = Connection();
        using var stopping = new CancellationTokenSource();
        connection.CloseOnStop(stopping.Token).Dispose();

        await stopping.CancelAsync();

        socket.Verify(x => x.CloseOutputAsync(It.IsAny<WebSocketCloseStatus>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (ClientConnection Connection, Mock<System.Net.WebSockets.WebSocket> Socket) Connection()
    {
        var socket = new Mock<System.Net.WebSockets.WebSocket>();
        socket.SetupGet(x => x.State).Returns(WebSocketState.Open);
        socket.Setup(x => x.CloseOutputAsync(It.IsAny<WebSocketCloseStatus>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return (new ClientConnection { Socket = socket.Object, ClientId = 1, WorkspaceId = 2 }, socket);
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

using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Wbskt.Protocol;

namespace Wbskt.E2E.FeatureTests.Fixtures;

/// <summary>
/// A device on the socket host without the SDK: a bare <see cref="ClientWebSocket"/> that opens the
/// connection the way <c>Wbskt.Client.Sdk</c> does (<c>{SocketWsBaseUrl}/ws</c>, the device token as a
/// bearer <c>Authorization</c> header, the <see cref="SocketProtocol.V1"/> subprotocol) and then sends
/// whatever it is given. The SDK and the host share <c>Wbskt.Protocol</c>, so a scenario driven only
/// through the SDK cannot see a protocol regression the two make together; this can send what the SDK
/// never would: malformed frames, oversized messages, a flood, a stale token.
/// </summary>
/// <remarks>
/// <see cref="ClientWebSocket"/> allows one send and one receive at a time, and cancelling a receive
/// aborts the connection, so a <see cref="ReceiveAsync"/> that times out leaves the socket aborted.
/// </remarks>
public sealed class RawDeviceSocket : IAsyncDisposable
{
    private readonly ClientWebSocket _socket;

    private RawDeviceSocket(ClientWebSocket socket)
    {
        _socket = socket;
    }

    public static Uri Endpoint => new($"{E2EConfig.SocketWsBaseUrl.TrimEnd('/')}/ws");

    public WebSocketState State => _socket.State;

    /// <summary>The status of the Close frame the host sent, once one has been received.</summary>
    public WebSocketCloseStatus? CloseStatus => _socket.CloseStatus;

    public string? CloseStatusDescription => _socket.CloseStatusDescription;

    /// <summary>The subprotocol the host accepted, or null for none.</summary>
    public string? SubProtocol => _socket.SubProtocol;

    /// <summary>Connects, or throws when the host refuses the upgrade.</summary>
    public static async Task<RawDeviceSocket> ConnectAsync(string token, string? subProtocol = SocketProtocol.V1)
    {
        var (status, socket) = await TryConnectAsync(token, subProtocol);
        return socket ?? throw new InvalidOperationException($"The socket host refused the upgrade with HTTP {(int)status}.");
    }

    /// <summary>
    /// Connects and returns the upgrade's status: 101 with the socket, or the status the host
    /// refused it with (401, 403, 400...) and no socket.
    /// </summary>
    public static async Task<(HttpStatusCode Status, RawDeviceSocket? Socket)> TryConnectAsync(string? token, string? subProtocol = SocketProtocol.V1)
    {
        var socket = new ClientWebSocket();
        if (token is not null)
        {
            socket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
        }

        if (subProtocol is not null)
        {
            socket.Options.AddSubProtocol(subProtocol);
        }

        socket.Options.CollectHttpResponseDetails = true;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await socket.ConnectAsync(Endpoint, timeout.Token);
        }
        catch (WebSocketException) when (socket.HttpStatusCode != 0)
        {
            var status = socket.HttpStatusCode;
            socket.Dispose();
            return (status, null);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return (HttpStatusCode.SwitchingProtocols, new RawDeviceSocket(socket));
    }

    /// <summary>Sends <paramref name="text"/> as one text message, verbatim.</summary>
    public Task SendRawAsync(string text, CancellationToken cancellationToken = default) =>
        _socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

    /// <summary>Sends a protocol message serialized as the SDK serializes it.</summary>
    public Task SendAsync(SocketMessage message, CancellationToken cancellationToken = default) =>
        SendRawAsync(JsonSerializer.Serialize(message), cancellationToken);

    /// <summary>
    /// The next whole message from the host, or null when none came within <paramref name="timeout"/>
    /// (which aborts the socket; see the remarks on the class).
    /// </summary>
    public async Task<MessageRead?> ReceiveAsync(TimeSpan timeout)
    {
        using var reader = new WebSocketMessageReader(_socket, SocketProtocol.MaxMessageBytes);
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            return await reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads and discards what the host sends (its pings, say) until its Close frame, and returns
    /// that frame's status: null when the connection ended without one or nothing ended it within
    /// <paramref name="timeout"/>. Safe to run while another task sends.
    /// </summary>
    public async Task<WebSocketCloseStatus?> ReceiveUntilClosedAsync(TimeSpan timeout)
    {
        using var reader = new WebSocketMessageReader(_socket, SocketProtocol.MaxMessageBytes);
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            while (true)
            {
                var read = await reader.ReadAsync(cts.Token);
                if (read.Kind == FrameKind.Closed)
                {
                    return _socket.CloseStatus;
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            return _socket.CloseStatus;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
            }
        }
        catch
        {
            // Going away regardless.
        }
        finally
        {
            _socket.Abort();
            _socket.Dispose();
        }
    }
}

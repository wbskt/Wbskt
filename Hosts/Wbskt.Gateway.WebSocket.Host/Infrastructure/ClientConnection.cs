using System.Net.WebSockets;
using System.Text;
using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Gateway.WebSocket.Host.Infrastructure;

/// <summary>
/// One client's socket. Its receive loop (SocketHandler) owns its end: it alone unregisters it,
/// disposes the socket, records the metric and announces the disconnect. Anyone else who wants it
/// closed calls <see cref="ClientConnectionBase.RequestCloseAsync"/>, which sends the Close frame and lets
/// the loop finish.
/// </summary>
public sealed class ClientConnection : ClientConnectionBase, IDisposable
{
    // How long a client has to answer our Close before its connection is cut.
    internal static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _closing = new();

    // Qualified: inside Wbskt.Gateway, a bare WebSocket names this host's own namespace.
    public required System.Net.WebSockets.WebSocket Socket { get; init; }

    public override bool IsOpen => Socket.State == WebSocketState.Open;

    /// <summary>Cancelled when a requested close was not answered in time, to cut the receive loop short.</summary>
    public CancellationToken Closing => _closing.Token;

    protected override async Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);

        // WebSocket allows only one outstanding send; commands, pings and samplers can race here.
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Sends a Close frame under the send lock, so it cannot interleave with a message being sent: the
    /// answer to a client's Close, or the last word on a connection that is ending.
    /// </summary>
    public async Task CloseOutputAsync(WebSocketCloseStatus status, string description, CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await Socket.CloseOutputAsync(status, description, cancellationToken);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Sends the Close frame (the client answers with its own, which ends the receive loop) and cuts
    /// the connection if no answer comes within <see cref="CloseGrace"/>.
    /// </summary>
    protected override async Task CloseAsync(DisconnectReason reason, string description)
    {
        try
        {
            _closing.CancelAfter(CloseGrace);
            await _sendLock.WaitAsync(_closing.Token);
            try
            {
                if (Socket.State == WebSocketState.Open)
                {
                    await Socket.CloseOutputAsync(WebSocketCloseStatuses.For(reason), description, _closing.Token);
                }
            }
            finally
            {
                _sendLock.Release();
            }
        }
        catch (ObjectDisposedException)
        {
            // The loop finished and disposed the connection first; nothing is left to close.
        }
        catch (Exception)
        {
            // The Close could not be sent, so no answer will come: cut the connection now.
            TryCancel();
        }
    }

    private void TryCancel()
    {
        try
        {
            _closing.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already finished.
        }
    }

    // The send lock is not disposed: a sender still waiting on it would throw for no benefit, and a
    // SemaphoreSlim that never handed out its wait handle holds nothing to release.
    public void Dispose() => _closing.Dispose();
}

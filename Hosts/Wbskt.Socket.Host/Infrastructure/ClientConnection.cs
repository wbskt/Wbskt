using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Wbskt.Protocol;

namespace Wbskt.Socket.Host.Infrastructure;

/// <summary>
/// One device's socket. Its receive loop (SocketHandler) owns its end: it alone unregisters it,
/// disposes the socket, records the metric and announces the disconnect. Anyone else who wants it
/// closed calls <see cref="RequestCloseAsync"/>, which sends the Close frame and lets the loop finish.
/// </summary>
public sealed class ClientConnection : IDisposable
{
    // How long a device has to answer our Close before its connection is cut.
    internal static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _closing = new();
    private readonly object _gate = new();

    public required WebSocket Socket { get; init; }
    public required int ClientId { get; init; }
    public required int WorkspaceId { get; init; }

    // Used by ConnectionSupersededHandler to decide which of two concurrent connections for the
    // same client (one per socket-host instance) is the older one to close.
    public DateTime EstablishedAtUtc { get; init; } = DateTime.UtcNow;

    // The sys.ping waiting for its pong; only the latest one counts.
    private (string Id, DateTime SentAt)? _pendingPing;

    /// <summary>The close someone asked for, if any; the receive loop reports its reason.</summary>
    public CloseRequest? CloseRequest { get; private set; }

    /// <summary>Cancelled when a requested close was not answered in time, to cut the receive loop short.</summary>
    public CancellationToken Closing => _closing.Token;

    /// <summary>
    /// Sends a <c>sys.ping</c> and remembers it, so the pong is timed on this host's clock rather
    /// than by a timestamp the device echoes back. A ping still unanswered is forgotten.
    /// </summary>
    public Task SendPingAsync(CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        var sentAt = DateTime.UtcNow;
        lock (_gate)
        {
            _pendingPing = (id, sentAt);
        }

        return SendAsync(new SocketMessage(MessageTypes.Ping, new PingPayload(sentAt, id)), cancellationToken);
    }

    /// <summary>
    /// Matches a pong to the ping waiting for it and returns when that ping was sent. A pong with an
    /// id must carry the pending ping's; one without (SDKs before the ping id) answers the pending
    /// ping. Either way a ping is answered once.
    /// </summary>
    public bool TryCompletePing(string? id, out DateTime sentAt)
    {
        lock (_gate)
        {
            if (_pendingPing is not { } pending || (id != null && id != pending.Id))
            {
                sentAt = default;
                return false;
            }

            _pendingPing = null;
            sentAt = pending.SentAt;
            return true;
        }
    }

    public async Task SendAsync(SocketMessage message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);

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
    /// Asks the connection to close: sends the Close frame (the device answers with its own, which
    /// ends the receive loop) and cuts the connection if no answer comes within <see cref="CloseGrace"/>.
    /// Only the first request counts; returns false for later ones.
    /// </summary>
    public async Task<bool> RequestCloseAsync(DisconnectReason reason, WebSocketCloseStatus status, string description)
    {
        lock (_gate)
        {
            if (CloseRequest != null)
            {
                return false;
            }

            CloseRequest = new CloseRequest(reason, status, description);
        }

        try
        {
            _closing.CancelAfter(CloseGrace);
            await _sendLock.WaitAsync(_closing.Token);
            try
            {
                if (Socket.State == WebSocketState.Open)
                {
                    await Socket.CloseOutputAsync(status, description, _closing.Token);
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

        return true;
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

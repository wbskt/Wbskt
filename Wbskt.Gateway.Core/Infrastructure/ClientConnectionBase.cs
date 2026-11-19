using Wbskt.Protocol;

namespace Wbskt.Gateway.Core.Infrastructure;

/// <summary>
/// What every transport's connection does the same way: the envelope's JSON, the pending ping, and
/// honouring only the first close asked for. A transport supplies the sending of a text message and
/// the close asked for while the session runs, and the transport's final close when it ends.
/// </summary>
public abstract class ClientConnectionBase : IClientConnection
{
    private readonly object _gate = new();
    private readonly TimeProvider _time;

    /// <summary>The most replies a connection can be owed; past it, a reply spends the budget again.</summary>
    public const int MaxRepliesOwed = 256;

    // The sys.ping waiting for its pong; only the latest one counts.
    private (string Id, DateTime SentAt)? _pendingPing;

    // Pongs and acks the client owes for what was sent to it. Written by the sending side, read by the
    // receive loop, so it changes only through Interlocked.
    private int _repliesOwed;

    public required int ClientId { get; init; }
    public required int WorkspaceId { get; init; }
    public DateTime EstablishedAtUtc { get; init; }
    public required DateTime TokenIssuedAtUtc { get; init; }

    /// <param name="time">The host's clock, for when the connection was made and pings were sent.</param>
    protected ClientConnectionBase(TimeProvider time)
    {
        _time = time;
        EstablishedAtUtc = time.GetUtcNow().UtcDateTime;
    }

    public abstract bool IsOpen { get; }

    public CloseRequest? CloseRequest { get; private set; }

    public Task SendPingAsync(CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        var sentAt = _time.GetUtcNow().UtcDateTime;
        lock (_gate)
        {
            // A ping still unanswered is replaced, and the pong owed for it carries over to this one.
            if (_pendingPing is null)
            {
                ExpectReply();
            }

            _pendingPing = (id, sentAt);
        }

        return SendAsync(new ProtocolMessage(MessageTypes.Ping, new PingPayload(sentAt, id)), cancellationToken);
    }

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

    public void ExpectReply()
    {
        int owed;
        do
        {
            owed = Volatile.Read(ref _repliesOwed);
            if (owed >= MaxRepliesOwed)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref _repliesOwed, owed + 1, owed) != owed);
    }

    public bool RepliesOwed => Volatile.Read(ref _repliesOwed) > 0;

    public bool TryTakeReply()
    {
        int owed;
        do
        {
            owed = Volatile.Read(ref _repliesOwed);
            if (owed <= 0)
            {
                return false;
            }
        }
        while (Interlocked.CompareExchange(ref _repliesOwed, owed - 1, owed) != owed);

        return true;
    }

    public Task SendAsync(ProtocolMessage message, CancellationToken cancellationToken)
    {
        return SendTextAsync(ProtocolJson.Serialize(message), cancellationToken);
    }

    public async Task<bool> RequestCloseAsync(DisconnectReason reason, string description)
    {
        lock (_gate)
        {
            if (CloseRequest != null)
            {
                return false;
            }

            CloseRequest = new CloseRequest(reason, description);
        }

        await CloseAsync(reason, description);
        return true;
    }

    /// <summary>Sends one text message (a serialized envelope) to the client.</summary>
    protected abstract Task SendTextAsync(string text, CancellationToken cancellationToken);

    /// <summary>
    /// Closes the transport for <paramref name="reason"/>. Called once, for the first close asked for;
    /// it must not throw, and must end the receive loop even when the client does not answer.
    /// </summary>
    protected abstract Task CloseAsync(DisconnectReason reason, string description);

    public abstract Task CloseTransportAsync(DisconnectReason reason);
}

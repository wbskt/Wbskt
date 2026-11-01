using System.Text.Json;
using Wbskt.Protocol;

namespace Wbskt.Gateway.Core.Infrastructure;

/// <summary>
/// What every transport's connection does the same way: the envelope's JSON, the pending ping, and
/// honouring only the first close asked for. A transport supplies the sending of a text message and
/// the close itself.
/// </summary>
public abstract class DeviceConnection : IDeviceConnection
{
    private readonly object _gate = new();

    // The sys.ping waiting for its pong; only the latest one counts.
    private (string Id, DateTime SentAt)? _pendingPing;

    public required int ClientId { get; init; }
    public required int WorkspaceId { get; init; }
    public DateTime EstablishedAtUtc { get; init; } = DateTime.UtcNow;

    public abstract bool IsOpen { get; }

    public CloseRequest? CloseRequest { get; private set; }

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

    public Task SendAsync(SocketMessage message, CancellationToken cancellationToken)
    {
        return SendTextAsync(JsonSerializer.Serialize(message), cancellationToken);
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

    /// <summary>Sends one text message (a serialized envelope) to the device.</summary>
    protected abstract Task SendTextAsync(string text, CancellationToken cancellationToken);

    /// <summary>
    /// Closes the transport for <paramref name="reason"/>. Called once, for the first close asked for;
    /// it must not throw, and must end the receive loop even when the device does not answer.
    /// </summary>
    protected abstract Task CloseAsync(DisconnectReason reason, string description);
}

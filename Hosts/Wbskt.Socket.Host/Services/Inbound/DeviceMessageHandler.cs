using System.Text.Json;
using Wbskt.Protocol;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;

namespace Wbskt.Socket.Host.Services.Inbound;

/// <summary>A device's own message (anything not <c>sys.*</c>): published for workflow triggers, the event log and the live feed.</summary>
public sealed class DeviceMessageHandler
{
    internal static readonly TimeSpan MaxSentAtAge = TimeSpan.FromDays(7);
    internal static readonly TimeSpan MaxSentAtClockAhead = TimeSpan.FromMinutes(1);

    private readonly IEventBus _eventBus;

    public DeviceMessageHandler(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public Task HandleAsync(InboundContext context, SocketMessage message, CancellationToken cancellationToken)
    {
        var connection = context.Connection;
        var payload = JsonSerializer.Serialize(message.Payload);
        return _eventBus.PublishAsync(new ClientMessageReceivedEvent(context.ClientRefId, connection.ClientId, connection.WorkspaceId, message.Type, payload)
        {
            SentAtUtc = PlausibleSentAt(message.SentAt, DateTime.UtcNow)
        }, cancellationToken);
    }

    /// <summary>
    /// The device's send time, if it is believable: not in the future beyond small clock drift and
    /// no older than a week. Anything else is dropped so a wrong device clock cannot rewrite history.
    /// </summary>
    internal static DateTime? PlausibleSentAt(DateTimeOffset? sentAt, DateTime nowUtc)
    {
        if (sentAt is not { } value)
        {
            return null;
        }

        var utc = value.UtcDateTime;
        if (utc > nowUtc + MaxSentAtClockAhead || utc < nowUtc - MaxSentAtAge)
        {
            return null;
        }

        // A device clock slightly ahead still means "now".
        return utc > nowUtc ? nowUtc : utc;
    }
}

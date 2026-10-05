using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[DeviceTraffic]
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientMessageReceivedEvent")]
public sealed record ClientMessageReceivedEvent(Guid ClientRefId, int ClientId, int WorkspaceId, string Type, string Payload) : BaseEvent, IClientContext
{
    /// <summary>
    /// When the device says it sent the message (UTC), if it said so and the time is plausible.
    /// Later than <see cref="BaseEvent.CreatedAtUtc"/> by a wide margin means the device was offline and buffered it.
    /// </summary>
    public DateTime? SentAtUtc { get; init; }
}

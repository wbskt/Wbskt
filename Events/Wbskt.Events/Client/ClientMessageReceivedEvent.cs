using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Client;

[ClientTraffic]
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientMessageReceivedEvent", Scope = NotifyScope.Client)]
public sealed record ClientMessageReceivedEvent(Guid ClientRefId, int ClientId, int WorkspaceId, string Type, string Payload) : BaseEvent, IClientContext
{
    /// <summary>
    /// When the client says it sent the message (UTC), if it said so and the time is plausible.
    /// Later than <see cref="BaseEvent.CreatedAtUtc"/> by a wide margin means the client was offline and buffered it.
    /// </summary>
    public DateTime? SentAtUtc { get; init; }
}

using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

// No longer published: ClientLatencyMeasuredEvent carries the same moment and the round trip.
// Kept so the rows already in the event log still name a known type.
[DeviceTraffic]
[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientPongEvent")]
public sealed record ClientPongEvent(Guid ClientRefId, int ClientId, int WorkspaceId, DateTime OriginalPingTime) : BaseEvent, IClientContext;

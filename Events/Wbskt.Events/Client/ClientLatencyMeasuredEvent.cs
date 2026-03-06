using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientLatencyMeasuredEvent(Guid ClientRefId, int WorkspaceId, double RoundTripMs) : ClientEvent(ClientRefId, WorkspaceId);

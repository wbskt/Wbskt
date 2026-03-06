using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientLatencyMeasuredEvent(Guid ClientRefId, int WorkspaceId, double RoundTripMs) : ClientEvent(ClientRefId, WorkspaceId);

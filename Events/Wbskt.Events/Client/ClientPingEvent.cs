using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientPingEvent")]
public sealed record ClientPingEvent(Guid ClientRefId, int ClientId, int WorkspaceId, DateTime PingTime) : ActorEvent, IClientContext;

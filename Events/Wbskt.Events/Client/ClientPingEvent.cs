using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
[SignalRNotify("OnClientPingEvent", Scope = NotifyScope.Client)]
public sealed record ClientPingEvent(Guid ClientRefId, int ClientId, int WorkspaceId, DateTime PingTime) : ActorEvent, IClientContext;

using Wbskt.EventBus.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientPingEvent(Guid ClientRefId, int WorkspaceId, DateTime PingTime) : ClientPayloadEvent(ClientRefId, WorkspaceId, "ping", PingTime.ToLongTimeString());

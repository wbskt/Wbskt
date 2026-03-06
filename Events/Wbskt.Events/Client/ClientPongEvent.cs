using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientPongEvent(Guid ClientRefId, int WorkspaceId, DateTime OriginalPingTime) : ClientEvent(ClientRefId, WorkspaceId);

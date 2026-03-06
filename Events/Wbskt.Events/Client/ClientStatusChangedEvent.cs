using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientStatusChangedEvent(Guid ClientRefId, int WorkspaceId, byte Status) : ClientEvent(ClientRefId, WorkspaceId);

using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientDisconnectedEvent(Guid ClientRefId, int WorkspaceId, string Reason) : ClientEvent(ClientRefId, WorkspaceId);

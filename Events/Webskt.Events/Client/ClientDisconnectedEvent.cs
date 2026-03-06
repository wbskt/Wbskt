using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientDisconnectedEvent(Guid ClientRefId, int WorkspaceId, string Reason) : ClientEvent(ClientRefId, WorkspaceId);

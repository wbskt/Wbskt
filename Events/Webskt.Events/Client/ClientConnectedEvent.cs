using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientConnectedEvent(Guid ClientRefId, int WorkspaceId) : ClientEvent(ClientRefId, WorkspaceId);

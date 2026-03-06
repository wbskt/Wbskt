using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientStatusChangedEvent(Guid ClientRefId, int WorkspaceId, byte Status) : ClientEvent(ClientRefId, WorkspaceId);

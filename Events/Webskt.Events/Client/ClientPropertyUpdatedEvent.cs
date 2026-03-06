using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientPropertyUpdatedEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    string PropertyName,
    string NewValue
) : ClientEvent(ClientRefId, WorkspaceId);

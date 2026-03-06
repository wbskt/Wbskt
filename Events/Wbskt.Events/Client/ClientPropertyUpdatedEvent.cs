using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Client;

[EventCriticality(EventCriticality.Info)]
public sealed record ClientPropertyUpdatedEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    string PropertyName,
    string NewValue
) : ClientEvent(ClientRefId, WorkspaceId);

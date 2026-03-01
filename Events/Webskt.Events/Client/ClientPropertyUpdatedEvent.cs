using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

public record ClientPropertyUpdatedEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    string PropertyName,
    string NewValue
) : ClientEvent(ClientRefId, WorkspaceId);

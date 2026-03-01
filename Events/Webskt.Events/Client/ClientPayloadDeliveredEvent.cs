using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

public record ClientPayloadDeliveredEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    string MessageType
) : ClientEvent(ClientRefId, WorkspaceId);

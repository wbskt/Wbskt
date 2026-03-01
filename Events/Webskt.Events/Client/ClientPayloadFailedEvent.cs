using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

public record ClientPayloadFailedEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    string MessageType,
    string Reason
) : ClientEvent(ClientRefId, WorkspaceId);

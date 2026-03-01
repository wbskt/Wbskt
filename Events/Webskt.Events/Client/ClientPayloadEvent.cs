using Webskt.Events.Abstractions;

namespace Webskt.Events.Client;

public record ClientPayloadEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    string MessageType,
    string Payload
) : ClientEvent(ClientRefId, WorkspaceId);

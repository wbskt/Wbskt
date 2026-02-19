using Webskt.Events.Abstractions;

namespace Webskt.Events.Shared;

public record DeviceMessageReceivedEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    string MessageType, 
    string Payload
) : DeviceDataEvent(ClientRefId, WorkspaceId);

using Webskt.Events.Abstractions;

namespace Webskt.Events.Shared;

public record DeviceMessageReceivedEvent(
    Guid ClientRefId, 
    string MessageType, 
    string Payload
) : DeviceDataEvent(ClientRefId);

namespace Webskt.Common.Abstraction.Events.Shared;

public record DeviceMessageReceivedEvent(
    Guid ClientRefId, 
    string MessageType, 
    string Payload
) : DeviceDataEvent(ClientRefId);

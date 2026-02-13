namespace Webskt.Common.Abstraction.Events.Shared;

public record DeviceCommandEvent(
    Guid TargetClientRefId, 
    string Action, 
    object? Payload = null
) : DeviceControlEvent(TargetClientRefId);

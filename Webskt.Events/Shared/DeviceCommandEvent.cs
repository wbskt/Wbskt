using Webskt.Events.Abstractions;

namespace Webskt.Events.Shared;

public record DeviceCommandEvent(
    Guid TargetClientRefId, 
    string Action, 
    object? Payload = null
) : DeviceControlEvent(TargetClientRefId);

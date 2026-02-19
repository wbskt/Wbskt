using Webskt.Events.Abstractions;

namespace Webskt.Events.Shared;

public record DeviceCommandEvent(
    Guid TargetClientRefId, 
    string Action, 
    int WorkspaceId,
    object? Payload = null
) : DeviceControlEvent(TargetClientRefId, WorkspaceId, Action);

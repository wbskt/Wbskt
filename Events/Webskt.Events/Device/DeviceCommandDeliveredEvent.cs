using Webskt.Events.Abstractions;

namespace Webskt.Events.Device;

public record DeviceCommandDeliveredEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    string CommandName
) : DeviceControlEvent(ClientRefId, WorkspaceId, CommandName);

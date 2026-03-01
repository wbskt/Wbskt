using Webskt.Events.Abstractions;

namespace Webskt.Events.Device;

public record DeviceCommandFailedEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    string CommandName,
    string Reason
) : DeviceControlEvent(ClientRefId, WorkspaceId, CommandName);

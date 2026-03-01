using Webskt.Events.Abstractions;

namespace Webskt.Events.Device;

public record DeviceCommandSentEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    string CommandName,
    string Payload
) : DeviceControlEvent(ClientRefId, WorkspaceId, CommandName);

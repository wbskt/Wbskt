using Webskt.Events.Abstractions;

namespace Webskt.Events.Device;

public record DevicePropertyUpdatedEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    string PropertyName,
    string NewValue
) : DeviceDataEvent(ClientRefId, WorkspaceId);

using Webskt.Events.Abstractions;

namespace Webskt.Events.Device;

public record DeviceTelemetryReceivedEvent(
    Guid ClientRefId, 
    int WorkspaceId,
    string Payload
) : DeviceDataEvent(ClientRefId, WorkspaceId);

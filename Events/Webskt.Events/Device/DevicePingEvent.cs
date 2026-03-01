using Webskt.Events.Abstractions;

namespace Webskt.Events.Device;

public record DevicePingEvent(Guid ClientRefId, int WorkspaceId, DateTime PingTime) : DeviceControlEvent(ClientRefId, WorkspaceId, "ping");

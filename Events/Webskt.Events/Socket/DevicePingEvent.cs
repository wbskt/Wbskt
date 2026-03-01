using Webskt.Events.Abstractions;

namespace Webskt.Events.Socket;

public record DevicePingEvent(Guid ClientRefId, int WorkspaceId, DateTime PingTime) : DeviceControlEvent(ClientRefId, WorkspaceId, "ping");
using Webskt.Events.Abstractions;

namespace Webskt.Events.Socket;

public record DevicePingCommand(Guid ClientRefId, int WorkspaceId, DateTime PingTime) : DeviceControlEvent(ClientRefId, WorkspaceId);

public record DevicePongEvent(Guid ClientRefId, int WorkspaceId, DateTime OriginalPingTime) : DeviceDataEvent(ClientRefId, WorkspaceId);

public record ClientLatencyMeasuredEvent(Guid ClientRefId, int WorkspaceId, double RoundTripMs) : ClientLifecycleEvent(ClientRefId, WorkspaceId);

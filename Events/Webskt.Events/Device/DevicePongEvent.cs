using Webskt.Events.Abstractions;

namespace Webskt.Events.Device;

public record DevicePongEvent(Guid ClientRefId, int WorkspaceId, DateTime OriginalPingTime) : DeviceDataEvent(ClientRefId, WorkspaceId);

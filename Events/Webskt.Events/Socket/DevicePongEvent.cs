using Webskt.Events.Abstractions;

namespace Webskt.Events.Socket;

public record DevicePongEvent(Guid ClientRefId, int WorkspaceId, DateTime OriginalPingTime) : DeviceDataEvent(ClientRefId, WorkspaceId);
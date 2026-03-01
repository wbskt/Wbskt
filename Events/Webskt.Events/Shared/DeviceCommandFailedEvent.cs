using Webskt.Events.Abstractions;

namespace Webskt.Events.Shared;

public record DeviceCommandFailedEvent(Guid ClientRefId, string Action, int WorkspaceId, string Reason) : DeviceControlEvent(ClientRefId, WorkspaceId, Action);
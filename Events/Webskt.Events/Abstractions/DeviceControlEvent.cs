namespace Webskt.Events.Abstractions;

public abstract record DeviceControlEvent(Guid ClientRefId, int WorkspaceId, string Action) : ClientLifecycleEvent(ClientRefId, WorkspaceId);
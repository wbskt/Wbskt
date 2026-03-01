namespace Webskt.Events.Abstractions;

public abstract record DeviceDataEvent(Guid ClientRefId, int WorkspaceId) : ClientLifecycleEvent(ClientRefId, WorkspaceId);
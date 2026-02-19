using Webskt.EventBus.Abstractions;

namespace Webskt.Events.Abstractions;

public abstract record AuthEvent : BaseEvent;
public abstract record UserEvent(int UserId) : AuthEvent;
public abstract record PermissionEvent : AuthEvent;

public abstract record RegistrationPolicyEvent(Guid PolicyRefId, int WorkspaceId) : BaseEvent;

public abstract record ClientLifecycleEvent(Guid ClientRefId, int WorkspaceId) : BaseEvent;
public abstract record DeviceDataEvent(Guid ClientRefId, int WorkspaceId) : ClientLifecycleEvent(ClientRefId, WorkspaceId);
public abstract record DeviceControlEvent(Guid ClientRefId, int WorkspaceId) : ClientLifecycleEvent(ClientRefId, WorkspaceId);

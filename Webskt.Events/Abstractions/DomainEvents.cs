using Webskt.EventBus.Abstractions;

namespace Webskt.Events.Abstractions;

public abstract record AuthEvent : BaseEvent;
public abstract record UserEvent(int UserId) : AuthEvent;
public abstract record PermissionEvent : AuthEvent;

public abstract record RegistrationPolicyEvent(Guid PolicyRefId) : BaseEvent;

public abstract record ClientLifecycleEvent(Guid ClientRefId) : BaseEvent;
public abstract record DeviceDataEvent(Guid ClientRefId) : ClientLifecycleEvent(ClientRefId);
public abstract record DeviceControlEvent(Guid ClientRefId) : ClientLifecycleEvent(ClientRefId);

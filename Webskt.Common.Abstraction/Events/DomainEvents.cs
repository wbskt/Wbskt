using Webskt.Common.Abstraction.Models.Management;

namespace Webskt.Common.Abstraction.Events;

// Base for security and identity events
public abstract record AuthEvent : BaseEvent;
public abstract record UserEvent(int UserId) : AuthEvent;
public abstract record PermissionEvent : AuthEvent;

// Base for registration policy changes
public abstract record RegistrationPolicyEvent(Guid PolicyRefId) : BaseEvent;

// Base for everything related to a specific client/device
public abstract record ClientLifecycleEvent(Guid ClientRefId) : BaseEvent;
public abstract record DeviceDataEvent(Guid ClientRefId) : ClientLifecycleEvent(ClientRefId);
public abstract record DeviceControlEvent(Guid ClientRefId) : ClientLifecycleEvent(ClientRefId);

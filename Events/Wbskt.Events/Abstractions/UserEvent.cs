namespace Wbskt.Events.Abstractions;

public abstract record UserEvent(int UserId) : AuthEvent;
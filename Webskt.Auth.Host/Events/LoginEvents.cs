using Webskt.Common.Abstraction.Events;

namespace Webskt.Auth.Host.Events;

public record UserLoginEvent(int UserId, string IpAddress, bool Success, string? FailureReason = null) : UserEvent(UserId);

public record TokenRotatedEvent(int UserId, string IpAddress) : UserEvent(UserId);

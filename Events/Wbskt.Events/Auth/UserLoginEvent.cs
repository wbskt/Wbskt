using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Auth;

[EventCriticality(EventCriticality.Info)]
public sealed record UserLoginEvent(int UserId, string IpAddress, bool Success, string? FailureReason = null) : UserEvent(UserId);
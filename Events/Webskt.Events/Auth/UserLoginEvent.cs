using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.Auth;

[EventCriticality(EventCriticality.Info)]
public sealed record UserLoginEvent(int UserId, string IpAddress, bool Success, string? FailureReason = null) : UserEvent(UserId);
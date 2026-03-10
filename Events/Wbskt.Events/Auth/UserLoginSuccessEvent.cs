using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Auth;

[EventCriticality(EventCriticality.Info)]
public sealed record UserLoginSuccessEvent(int UserId, string IpAddress) : UserEvent(UserId);

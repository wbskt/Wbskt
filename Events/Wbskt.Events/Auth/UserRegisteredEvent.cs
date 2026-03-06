using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Auth;

[EventCriticality(EventCriticality.Info)]
public sealed record UserRegisteredEvent(int UserId, string Username, string Email) : UserEvent(UserId);

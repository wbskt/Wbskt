using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.Auth;

[EventCriticality(EventCriticality.Info)]
public sealed record UserRegisteredEvent(int UserId, string Username, string Email) : UserEvent(UserId);

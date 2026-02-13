using Webskt.Common.Abstraction.Events;

namespace Webskt.Auth.Host.Events;

public record UserRegisteredEvent(int UserId, string Username, string Email) : UserEvent(UserId);

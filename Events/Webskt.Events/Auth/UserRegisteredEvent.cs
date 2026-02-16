using Webskt.Events.Abstractions;

namespace Webskt.Events.Auth;

public record UserRegisteredEvent(int UserId, string Username, string Email) : UserEvent(UserId);

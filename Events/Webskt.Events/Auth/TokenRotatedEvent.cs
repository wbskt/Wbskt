using Webskt.Events.Abstractions;

namespace Webskt.Events.Auth;

public record TokenRotatedEvent(int UserId, string IpAddress) : UserEvent(UserId);
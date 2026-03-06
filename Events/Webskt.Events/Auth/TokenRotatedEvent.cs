using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.Auth;

[EventCriticality(EventCriticality.Info)]
public sealed record TokenRotatedEvent(int UserId, string IpAddress) : UserEvent(UserId);
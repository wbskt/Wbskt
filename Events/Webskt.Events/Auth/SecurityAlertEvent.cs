using Webskt.EventBus.Abstractions;
using Webskt.Events.Abstractions;

namespace Webskt.Events.Auth;

[EventCriticality(EventCriticality.Warning)]
public sealed record SecurityAlertEvent(
    string AlertType, 
    string Message, 
    string? IpAddress = null, 
    string? Metadata = null
) : AuthEvent;

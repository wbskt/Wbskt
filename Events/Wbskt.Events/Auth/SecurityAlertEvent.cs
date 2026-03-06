using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;

namespace Wbskt.Events.Auth;

[EventCriticality(EventCriticality.Warning)]
public sealed record SecurityAlertEvent(
    string AlertType, 
    string Message, 
    string? IpAddress = null, 
    string? Metadata = null
) : AuthEvent;

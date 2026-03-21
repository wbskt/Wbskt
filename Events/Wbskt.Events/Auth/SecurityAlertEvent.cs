using Wbskt.EventBus.Abstractions;

namespace Wbskt.Events.Auth;

[EventCriticality(EventCriticality.Error)]
public sealed record SecurityAlertEvent(
    string AlertType, 
    string Message, 
    string? IpAddress = null, 
    string? Metadata = null
) : BaseEvent;

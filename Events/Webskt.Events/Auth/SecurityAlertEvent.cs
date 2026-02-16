using Webskt.Events.Abstractions;

namespace Webskt.Events.Auth;

public record SecurityAlertEvent(
    string AlertType, 
    string Message, 
    string? IpAddress = null, 
    string? Metadata = null
) : AuthEvent;

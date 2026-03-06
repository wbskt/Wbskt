using Webskt.EventBus.Abstractions;

namespace Webskt.Events.System;

[EventCriticality(EventCriticality.Error)]
public sealed record SystemErrorEvent(
    string ErrorType, 
    string Message, 
    string? StackTrace, 
    string? RequestPath, 
    string? RequestId
) : BaseEvent;

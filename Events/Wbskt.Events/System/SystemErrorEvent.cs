using Wbskt.EventBus.Abstractions;

namespace Wbskt.Events.System;

[EventCriticality(EventCriticality.Error)]
public sealed record SystemErrorEvent(
    string ErrorType, 
    string Message, 
    string? StackTrace, 
    string? RequestPath, 
    string? RequestId
) : BaseEvent;

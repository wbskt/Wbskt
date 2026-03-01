using Webskt.EventBus.Abstractions;

namespace Webskt.Events.System;

public record SystemErrorEvent(
    string ErrorType, 
    string Message, 
    string? StackTrace, 
    string? RequestPath, 
    string? RequestId
) : BaseEvent;

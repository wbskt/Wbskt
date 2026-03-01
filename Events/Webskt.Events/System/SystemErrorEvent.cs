using Webskt.EventBus.Abstractions;

namespace Webskt.Events.System;

public record SystemErrorEvent(
    string Component,
    string ErrorMessage,
    string? StackTrace = null
) : BaseEvent;

using Wbskt.EventBus.Abstractions;
using Wbskt.Models;

namespace Wbskt.Events.System;

/// <summary>
/// A request failed with an unexpected fault. It names the fault's type, the path and the trace id
/// only: the event log is readable by tenant admins, and the message and stack, which can quote SQL,
/// addresses or file paths, stay in the host's log line, found by <see cref="RequestId"/>.
/// </summary>
[EventCriticality(EventCriticality.Error)]
public sealed record SystemErrorEvent(
    string ErrorType,
    string? RequestPath,
    string? RequestId
) : BaseEvent;

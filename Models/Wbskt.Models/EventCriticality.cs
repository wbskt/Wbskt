namespace Wbskt.Models;

/// <summary>
/// How serious an event is: declared on an event type with <c>EventCriticalityAttribute</c> and
/// returned on every event-log entry. It is part of the public event-log contract, so it lives here
/// beside the other shared contract types rather than in the event bus. Serialized as its number:
/// Info = 0, Warning = 1, Error = 2.
/// </summary>
public enum EventCriticality : short
{
    Info = 0,
    Warning = 1,
    Error = 2
}

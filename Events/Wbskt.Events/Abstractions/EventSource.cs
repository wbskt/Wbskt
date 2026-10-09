namespace Wbskt.Events.Abstractions;

/// <summary>
/// Where an action came from, as the audit log shows it. Stored as a number, so values are only ever
/// added, never renumbered.
/// </summary>
public enum EventSource : byte
{
    /// <summary>A person, through the console in a browser.</summary>
    Console = 1,

    /// <summary>A person's token used outside the console: a script, a CLI, an integration.</summary>
    Api = 2,

    /// <summary>A device: its messages, connections and registrations.</summary>
    Device = 3,

    /// <summary>A workflow run, such as a command a run sent.</summary>
    Workflow = 4,

    /// <summary>The platform itself: a background job, a sweep, a host starting.</summary>
    System = 5
}

/// <summary>One field a change event changed: its name and its value before and after, as text.</summary>
public sealed record FieldChange(string Field, string? Before, string? After);

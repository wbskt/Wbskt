using Wbskt.EventBus.Abstractions;

namespace Wbskt.Management.Host.Models;

public record EventLogResponse(
    string EventName,
    string EventData,
    EventCriticality Criticality,
    Guid? PolicyRefId,
    Guid? ClientRefId,
    Guid? WorkflowRefId,
    DateTime CreatedAtUtc,
    // Who caused it, for an action taken through the API (or the user a sign-in event is about).
    Guid? UserRefId = null,
    // The entry's own id, so a detail view can name one entry. Page cursors are made from it.
    long Id = 0
);

/// <summary>A row of the event log with the Id its page cursor is made from.</summary>
public sealed record EventLogRow(long Id, EventLogResponse Entry);

/// <summary>Whether a read of the event log keeps raw device traffic (messages, pings, readings) or drops it.</summary>
public enum EventLogTraffic
{
    Include,
    Exclude
}

/// <summary>
/// The filters <c>GET event-logs</c> and <c>GET event-logs/csv</c> take. Every one narrows the
/// result, and they combine with AND, except that <see cref="Group"/> and <see cref="EventNames"/>
/// together keep an event named by either.
/// </summary>
public sealed class EventLogQuery
{
    /// <summary>The older single filter: entries whose event name contains this.</summary>
    public string? EventName { get; set; }

    /// <summary>Exact event names, repeated or comma-separated: <c>eventNames=ClientRenamedEvent,ClientDeletedEvent</c>.</summary>
    public string[]? EventNames { get; set; }

    /// <summary>Named groups of events, repeated or comma-separated: people, clients, policies, workflows, security.</summary>
    public string[]? Group { get; set; }

    public EventCriticality? Criticality { get; set; }

    /// <summary>Only this policy's entries. A policy this workspace does not own is a 404.</summary>
    public Guid? PolicyRefId { get; set; }

    /// <summary>Only this client's entries. A client this workspace does not own is a 404.</summary>
    public Guid? ClientRefId { get; set; }

    /// <summary>Only this workflow's entries, every version. Unlike a client or policy, a deleted workflow can still be named.</summary>
    public Guid? WorkflowRefId { get; set; }

    /// <summary>Only entries this user caused (or, for a sign-in, is about).</summary>
    public Guid? UserRefId { get; set; }

    /// <summary>Entries at or after this instant.</summary>
    public DateTimeOffset? From { get; set; }

    /// <summary>Entries before this instant.</summary>
    public DateTimeOffset? To { get; set; }

    /// <summary><c>include</c> (the default) or <c>exclude</c> raw device traffic.</summary>
    public EventLogTraffic Traffic { get; set; } = EventLogTraffic.Include;

    /// <summary>Text to find in the entry's data or event name: a client, policy or workflow name, an email, an id.</summary>
    public string? Q { get; set; }
}

/// <summary>A resolved <see cref="EventLogQuery"/>: references are internal ids and groups are event names.</summary>
public sealed record EventLogFilter(
    string? EventName = null,
    IReadOnlyCollection<string>? EventNames = null,
    IReadOnlyCollection<string>? ExcludeEventNames = null,
    EventCriticality? Criticality = null,
    int? PolicyId = null,
    int? ClientId = null,
    Guid? WorkflowRefId = null,
    Guid? UserRefId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? Search = null);

/// <summary>How many entries each group of events has in a window, for the audit log's view counts.</summary>
public sealed record EventLogSummaryResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    long Total,
    IReadOnlyDictionary<string, long> Groups);

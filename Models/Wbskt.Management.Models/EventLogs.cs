using Wbskt.Models;

namespace Wbskt.Management.Models;

/// <summary>
/// One entry of the event log, as <c>GET event-logs</c> and a client's <c>comms</c> return it.
/// <see cref="Criticality"/> is serialized as its number.
/// </summary>
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
    long Id = 0,
    // Where it came from: Console, Api, Device, Workflow or System; null when not known.
    string? Source = null,
    // The caller's address and user agent, for an action taken through the API.
    string? ClientAddress = null,
    string? UserAgent = null
);

/// <summary>
/// How many entries a window holds, for the audit log's view counts: each group, each event, each
/// person who caused one (by user reference), and each source. All but the groups leave device
/// traffic out when the summary is asked to.
/// </summary>
public sealed record EventLogSummaryResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    long Total,
    IReadOnlyDictionary<string, long> Groups,
    IReadOnlyDictionary<string, long>? Events = null,
    IReadOnlyDictionary<Guid, long>? People = null,
    IReadOnlyDictionary<string, long>? Sources = null);

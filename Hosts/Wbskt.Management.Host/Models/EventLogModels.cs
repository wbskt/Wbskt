using Wbskt.EventBus.Abstractions;
using Wbskt.Models;

namespace Wbskt.Management.Host.Models;

public record EventLogResponse(
    string EventName,
    string EventData,
    EventCriticality Criticality,
    Guid? PolicyRefId,
    Guid? ClientRefId,
    Guid? WorkflowRefId,
    DateTime CreatedAtUtc
);

/// <summary>A row of the event log with the Id its page cursor is made from.</summary>
public sealed record EventLogRow(long Id, EventLogResponse Entry);

/// <summary>
/// A page of the event log, newest first. Pass <see cref="NextCursor"/> back as <c>cursor</c> for the
/// next page; it is null on the last one.
/// </summary>
public sealed record EventLogListResponse : ListResponse<EventLogResponse>
{
    public long? NextCursor { get; init; }
}

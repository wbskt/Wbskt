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
    DateTime CreatedAtUtc,
    // Who caused it, for an action taken through the API (or the user a sign-in event is about).
    Guid? UserRefId = null
);

/// <summary>A row of the event log with the Id its page cursor is made from.</summary>
public sealed record EventLogRow(long Id, EventLogResponse Entry);

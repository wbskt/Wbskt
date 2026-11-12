using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Engine.Contracts;

/// <summary>
/// What the engine did with an inbound call. One shape for every inbound endpoint. Runs are named by
/// their RefId only: the engine's own row ids never leave it.
/// </summary>
/// <param name="Outcome">
/// A summary of <paramref name="Registrations"/>: <see cref="InboundOutcome.StartedRun"/> if any started a run,
/// else the first of Queued, Dropped, Refused, Filtered and SecretMismatch, else what happened before any
/// registration was reached (a resumed wait, a duplicate, no registration, a busy workspace).
/// </param>
/// <param name="RunRefId">The first run started, for callers that expect one.</param>
/// <param name="Registrations">
/// One entry per trigger registration the call matched, since one call can start several runs. Empty
/// for a signal, which only resumes a waiting run.
/// </param>
public sealed record InboundResponse(InboundOutcome Outcome, Guid? RunRefId, IReadOnlyList<InboundRegistrationOutcome> Registrations)
{
    /// <summary>Whether the call resumed a waiting run.</summary>
    [JsonIgnore]
    public bool Matched => Outcome == InboundOutcome.ResumedBookmark;
}

/// <summary>What one matched trigger registration did with the call.</summary>
/// <param name="RefusalReason">Which plan limit refused the start, when <paramref name="Outcome"/> is <see cref="InboundOutcome.Refused"/>.</param>
public sealed record InboundRegistrationOutcome(Guid WorkflowRefId, InboundOutcome Outcome, Guid? RunRefId, string CorrelationKey, InboundRefusalReason? RefusalReason = null);

[JsonConverter(typeof(JsonStringEnumConverter<InboundOutcome>))]
public enum InboundOutcome
{
    ResumedBookmark,
    StartedRun,
    Queued,
    Dropped,
    Cancelled,
    NoRegistration,

    /// <summary>The same idempotency key was already used, so nothing new started.</summary>
    Idempotent,

    /// <summary>The registration matched, but its filter expression rejected the payload.</summary>
    Filtered,

    /// <summary>The registration matched, but the caller did not present its webhook secret.</summary>
    SecretMismatch,

    /// <summary>The plan refused the start; the registration's <c>RefusalReason</c> says which limit.</summary>
    Refused,

    /// <summary>
    /// Too many runs were starting in the workspace at once, so a start did not happen and its claim was
    /// released. Answered by the manual endpoint only; send the call again with the same idempotency key.
    /// </summary>
    Busy
}

/// <summary>Which plan limit refused a start.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InboundRefusalReason>))]
public enum InboundRefusalReason
{
    /// <summary>The plan already has its limit of runs going at once, across the tenant's workspaces.</summary>
    ActiveRunLimit,

    /// <summary>The plan has spent its workflow credits for the month.</summary>
    CreditLimit,

    /// <summary>The workspace has spent its own monthly credit cap.</summary>
    WorkspaceCreditCap
}

using System.Text.Json.Serialization;

namespace Wbskt.Workflow.Engine.Contracts;

/// <summary>
/// What the engine did with an inbound call. One shape for every inbound endpoint. Runs are named by
/// their RefId only: the engine's own row ids never leave it.
/// </summary>
/// <param name="Outcome">
/// A summary of <paramref name="Registrations"/>: <see cref="InboundOutcome.StartedRun"/> if any started a run,
/// else the first of Queued, Dropped, Filtered and SecretMismatch, else what happened before any
/// registration was reached (a resumed wait, a duplicate, no registration).
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
public sealed record InboundRegistrationOutcome(Guid WorkflowRefId, InboundOutcome Outcome, Guid? RunRefId, string CorrelationKey);

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
    SecretMismatch
}

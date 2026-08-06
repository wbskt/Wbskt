namespace Wbskt.Management.Models.Workflow;

/// <summary>
/// The outcome of asking the engine to start a run. Not every non-start is an error: a queued or
/// dropped request is the concurrency policy doing its job, and a duplicate idempotency key is the
/// caller's retry being deduplicated as designed.
/// </summary>
public enum StartRunOutcome
{
    /// <summary>A new run was created. <c>RunRefId</c>/<c>RunId</c> identify it.</summary>
    Started,

    /// <summary>
    /// The same idempotency key was already used, so no new run was created. <c>RunRefId</c> is the
    /// run the original call started, when it could be resolved.
    /// </summary>
    Duplicate,

    /// <summary>
    /// The trigger's concurrency policy queued this request behind an active run; it will start when
    /// that one finishes.
    /// </summary>
    Queued,

    /// <summary>The trigger's concurrency policy discarded this request.</summary>
    Dropped,

    /// <summary>The workflow has no manual trigger, so it cannot be started this way.</summary>
    NoManualTrigger
}

public record StartRunResponse(Guid RunRefId, long RunId, StartRunOutcome Outcome = StartRunOutcome.Started);
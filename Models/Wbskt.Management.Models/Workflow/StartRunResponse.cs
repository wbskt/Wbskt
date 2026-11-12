namespace Wbskt.Management.Models.Workflow;

/// <summary>
/// The outcome of asking the engine to start a run. Not every non-start is an error: a queued or
/// dropped request is the concurrency policy doing its job, and a duplicate idempotency key is the
/// caller's retry being deduplicated as designed.
/// </summary>
public enum StartRunOutcome
{
    /// <summary>A new run was created. <c>RunRefId</c> identifies it.</summary>
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
    NoManualTrigger,

    /// <summary>The plan already has its limit of runs going at once, across the tenant's workspaces.</summary>
    PlanRunLimitReached,

    /// <summary>The plan has spent its workflow credits for the month.</summary>
    PlanCreditLimitReached,

    /// <summary>The workspace has spent its own monthly credit cap.</summary>
    WorkspaceCreditCapReached,

    /// <summary>Too many runs were starting in the workspace at once; send it again, with the same idempotency key.</summary>
    Busy
}

public record StartRunResponse(Guid RunRefId, StartRunOutcome Outcome = StartRunOutcome.Started);
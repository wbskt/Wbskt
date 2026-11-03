namespace Wbskt.Workflow.Abstraction.Entities;

/// <summary>What a run list shows beyond the run row (<c>Run_GetListDetails</c>).</summary>
public sealed record RunListDetailRow
{
    public required int RunId { get; init; }
    public decimal? CreditsConsumed { get; init; }

    /// <summary>The step of the run's first <c>NodeFailed</c>, for a failed or failing run.</summary>
    public Guid? FailedNodeId { get; init; }
    public string? FailedErrorCode { get; init; }

    /// <summary>The last step a succeeded run completed: its End step, or the last of its last branch.</summary>
    public Guid? EndedNodeId { get; init; }

    /// <summary>For a live run, the step its most recently moved branch is on.</summary>
    public Guid? CurrentNodeId { get; init; }

    /// <summary>When that step is being retried: the attempt under way and how many it may have.</summary>
    public int? CurrentAttempt { get; init; }
    public int? CurrentMaxAttempts { get; init; }
}

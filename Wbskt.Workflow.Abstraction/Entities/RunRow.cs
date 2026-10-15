namespace Wbskt.Workflow.Abstraction.Entities;

public record RunRow
{
    public required int Id { get; init; }
    public required Guid RefId { get; init; }
    public required int WorkflowDefinitionId { get; init; }
    public required Guid WorkflowRefId { get; init; }
    public required int WorkflowVersion { get; init; }
    public required Guid TriggerNodeId { get; init; }
    public required string? CorrelationKey { get; init; }
    public required string Status { get; init; }
    public required DateTime StartedAt { get; init; }
    public required DateTime? CompletedAt { get; init; }
    public required DateTime? CancellationRequestedAt { get; init; }
    public required string? CancellationReason { get; init; }
    public required decimal CreditBudget { get; init; }
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// The run whose sub-workflow step started this one; null for a run started any other way.
    /// Cancelling or failing that run cancels this one with it.
    /// </summary>
    public Guid? ParentRunRefId { get; init; }

    /// <summary>
    /// The JSON value the run's <c>control:end</c> node handed back, if it declared one. A parent's
    /// sub-workflow step reads it as <c>childResult.output</c>. Only read by the procs that return a
    /// single run; the list procs leave it out.
    /// </summary>
    public string? OutputJson { get; init; }
}

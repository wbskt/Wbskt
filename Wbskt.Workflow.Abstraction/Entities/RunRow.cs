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
}

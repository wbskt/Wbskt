namespace Wbskt.Workflow.Abstraction.Entities;

public record TriggerRegistrationRow
{
    public required int Id { get; init; }
    public required int WorkflowDefinitionId { get; init; }
    public required Guid WorkflowRefId { get; init; }
    public required int WorkflowVersion { get; init; }
    public required Guid TriggerNodeId { get; init; }
    public required string TriggerKind { get; init; }
    public required string TriggerKey { get; init; }
    public required string? CorrelationExpression { get; init; }
    public required string ConcurrencyPolicy { get; init; }
    public required string? FilterExpression { get; init; }
    public required DateTime CreatedAt { get; init; }
}

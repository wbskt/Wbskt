namespace Wbskt.Workflow.Abstraction.Entities;

public record PendingTriggerEventRow
{
    public required int Id { get; init; }
    public required Guid WorkflowRefId { get; init; }
    public required Guid TriggerNodeId { get; init; }
    public required string CorrelationKey { get; init; }
    public required string InboundEventJson { get; init; }
    public required DateTime EnqueuedAt { get; init; }
    public required DateTime CreatedAt { get; init; }
}

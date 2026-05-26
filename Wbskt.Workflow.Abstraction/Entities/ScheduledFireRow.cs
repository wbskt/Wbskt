namespace Wbskt.Workflow.Abstraction.Entities;

public record ScheduledFireRow
{
    public required int Id { get; init; }
    public required Guid TriggerNodeId { get; init; }
    public required int WorkflowDefinitionId { get; init; }
    public required Guid WorkflowRefId { get; init; }
    public required string CronOrInterval { get; init; }
    public required DateTime NextFireAt { get; init; }
    public required DateTime? LeasedUntil { get; init; }
    public required DateTime CreatedAt { get; init; }
}

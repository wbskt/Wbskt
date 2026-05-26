namespace Wbskt.Workflow.Abstraction.Entities;

public record HistoryEventRow
{
    public required long HistoryEventId { get; init; }
    public required int RunId { get; init; }
    public required Guid? BranchRefId { get; init; }
    public required Guid? NodeId { get; init; }
    public required string EventKind { get; init; }
    public required string Severity { get; init; }
    public required string? PayloadJson { get; init; }
    public required DateTime Timestamp { get; init; }
}

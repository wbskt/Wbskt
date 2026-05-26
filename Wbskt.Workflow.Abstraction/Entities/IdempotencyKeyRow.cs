namespace Wbskt.Workflow.Abstraction.Entities;

public record IdempotencyKeyRow
{
    public required int Id { get; init; }
    public required string KeyValue { get; init; }
    public required int RunId { get; init; }
    public required Guid BranchRefId { get; init; }
    public required Guid NodeId { get; init; }
    public required int Attempt { get; init; }
    public required string Status { get; init; }
    public required string? ResultJson { get; init; }
    public required string? ErrorJson { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime? CompletedAt { get; init; }
}

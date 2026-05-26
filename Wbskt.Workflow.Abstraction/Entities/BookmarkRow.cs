namespace Wbskt.Workflow.Abstraction.Entities;

public record BookmarkRow
{
    public required int Id { get; init; }
    public required Guid RefId { get; init; }
    public required int RunId { get; init; }
    public required Guid BranchRefId { get; init; }
    public required Guid NodeId { get; init; }
    public required string WakeConditionKind { get; init; }
    public required string MatchKey { get; init; }
    public required string WakeConditionJson { get; init; }
    public required DateTime? ExpiresAt { get; init; }
    public required string? TtlPort { get; init; }
    public required DateTime CreatedAt { get; init; }
}

namespace Wbskt.Workflow.Abstraction.Entities;

public record BookmarkRow
{
    public required long Id { get; init; }
    public required Guid RefId { get; init; }
    public required int RunId { get; init; }
    public required Guid BranchRefId { get; init; }

    /// <summary>The waiting branch's id, where the read returns it (a claim, a match); null otherwise.</summary>
    public long? BranchId { get; init; }
    public required Guid NodeId { get; init; }
    public required string WakeConditionKind { get; init; }
    public required string MatchKey { get; init; }
    public required string WakeConditionJson { get; init; }
    public required DateTime? ExpiresAt { get; init; }
    public required string? TtlPort { get; init; }
    public required DateTime CreatedAt { get; init; }
}

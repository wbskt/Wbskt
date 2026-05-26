namespace Wbskt.Workflow.Abstraction.Entities;

public record BranchRow
{
    public required int Id { get; init; }
    public required Guid RefId { get; init; }
    public required int RunId { get; init; }
    public required Guid? ParentBranchId { get; init; }
    public required Guid? ForkCohortId { get; init; }
    public required Guid NodeId { get; init; }
    public required string Status { get; init; }
    public required string? PendingTakePort { get; init; }
    public required string LocalJson { get; init; }
    public required string? LastOutputJson { get; init; }
    public required string? CompensationStackJson { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
    public required byte[] RowVersion { get; init; }
}

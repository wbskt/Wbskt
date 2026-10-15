using Wbskt.Workflow.Abstraction.Runtime;

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

    /// <summary>
    /// A branch about to be inserted: Active at <paramref name="nodeId"/>, with no pending port and no
    /// output yet. The columns the database assigns (Id, RowVersion) are left empty here, so every
    /// place that starts a branch - a run's root branch, a fork child, a join continuation - fills
    /// them in the same way.
    /// </summary>
    public static BranchRow New(
        Guid refId,
        int runId,
        Guid nodeId,
        string localJson,
        DateTime nowUtc,
        Guid? parentBranchId = null,
        Guid? forkCohortId = null,
        string? compensationStackJson = null)
    {
        return new BranchRow
        {
            Id = 0,
            RefId = refId,
            RunId = runId,
            ParentBranchId = parentBranchId,
            ForkCohortId = forkCohortId,
            NodeId = nodeId,
            Status = BranchStatus.Active,
            PendingTakePort = null,
            LocalJson = localJson,
            LastOutputJson = null,
            CompensationStackJson = compensationStackJson,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
            // Branches.RowVersion is a SQL Server ROWVERSION: the database assigns it.
            RowVersion = Array.Empty<byte>()
        };
    }
}

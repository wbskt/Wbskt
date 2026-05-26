using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface IBranchProvider
{
    Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct);
    Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct);
    Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct);
    Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct);
    Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct);
    Task<IReadOnlyCollection<BranchRow>> GetActiveByRunIdAsync(int runId, CancellationToken ct);
    Task<IReadOnlyCollection<BranchRow>> GetAllActiveAsync(CancellationToken ct);
    Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct);
    Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct);
    Task<BranchRow> SetFailedAsync(long branchId, string? lastOutputJson, CancellationToken ct);
}

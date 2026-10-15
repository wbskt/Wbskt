using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface IBranchProvider
{
    Task<BranchRow> CreateAsync(BranchRow row, CancellationToken ct);
    Task<BranchRow> UpsertAsync(BranchRow row, CancellationToken ct);
    Task<BranchRow> GetByIdAsync(long branchId, CancellationToken ct);
    Task<BranchRow> GetByRefIdAsync(Guid refId, CancellationToken ct);
    Task<IReadOnlyCollection<BranchRow>> GetAllByRunIdAsync(int runId, CancellationToken ct);
    Task<IReadOnlyCollection<BranchRow>> GetRunningBranchesAsync(CancellationToken ct);

    /// <summary>
    /// Waiting branches of a live run that have no bookmark to wake them and have not been written since
    /// <paramref name="cutoffUtc"/>: branches stranded by a park that never wrote its bookmark, or by a
    /// wake whose claim deleted the bookmark and whose dispatch was lost. Defaulted so the test doubles
    /// that never sweep need no stub.
    /// </summary>
    Task<IReadOnlyCollection<BranchRow>> GetWaitingWithoutBookmarkAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
        => Task.FromResult<IReadOnlyCollection<BranchRow>>(Array.Empty<BranchRow>());
    Task<BranchRow> UpdatePointerAsync(long branchId, Guid currentNodeId, string status, string localJson, string? lastOutputJson, CancellationToken ct);
    Task<BranchRow> SetCompletedAsync(long branchId, CancellationToken ct);
    Task<BranchRow> SetFailedAsync(long branchId, string? lastOutputJson, CancellationToken ct);
    Task<int> CancelWaitingBranchesAsync(int runId, CancellationToken ct) => Task.FromResult(0);
}

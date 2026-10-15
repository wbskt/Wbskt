using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Runtime;

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

    /// <summary>
    /// Creates <paramref name="row"/> unless a branch with its RefId already exists, and returns the
    /// stored row with whether this call created it. An existing row is returned untouched. A Fork
    /// replayed after a crash asks for children it already created; this is how it creates, counts and
    /// dispatches only the ones that are new.
    /// </summary>
    /// <remarks>The default always creates, which is what a fake that never replays a Fork needs.</remarks>
    async Task<(BranchRow Row, bool Created)> CreateIfAbsentAsync(BranchRow row, CancellationToken ct) =>
        (await CreateAsync(row, ct), true);

    /// <summary>
    /// Ends the branch with <paramref name="toStatus"/> only if it is still live (Active, Waiting or
    /// Compensating), and says whether it did. Two loops can reach the end of one branch - a cancel
    /// racing a bookmark wake, a recovery requeue racing a slow node on the old leader - and only the
    /// one that actually ended it may release it; a second release lets the run's active-branch count
    /// reach zero while another branch is still running. <paramref name="lastOutputJson"/> replaces the
    /// branch's last output, as the unguarded updates do.
    /// </summary>
    /// <remarks>
    /// The default reads then writes, which is enough for a single-threaded fake; the SQL provider does
    /// it as one compare-and-set.
    /// </remarks>
    async Task<bool> EndIfLiveAsync(long branchId, string toStatus, string? lastOutputJson, CancellationToken ct)
    {
        BranchRow row = await GetByIdAsync(branchId, ct);
        if (!BranchStatus.IsLive(row.Status))
        {
            return false;
        }

        await UpdatePointerAsync(branchId, row.NodeId, toStatus, row.LocalJson, lastOutputJson, ct);
        return true;
    }
}

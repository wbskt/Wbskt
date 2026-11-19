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

    /// <summary>
    /// The statuses the run's branches are in, each once. The finalizer needs no more to pick a run's
    /// terminal status. Defaulted over <see cref="GetAllByRunIdAsync"/> for the test doubles.
    /// </summary>
    async Task<IReadOnlyCollection<string>> GetStatusesByRunIdAsync(int runId, CancellationToken ct) =>
        (await GetAllByRunIdAsync(runId, ct)).Select(branch => branch.Status).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Active branches, with their run's status, paged by Id after <paramref name="afterId"/>: what a new
    /// leader recovers. Defaulted so the test doubles that never recover need no stub.
    /// </summary>
    Task<IReadOnlyCollection<RunningBranch>> GetRunningBranchesAsync(long afterId, int batchSize, CancellationToken ct)
        => Task.FromResult<IReadOnlyCollection<RunningBranch>>(Array.Empty<RunningBranch>());

    /// <summary>
    /// Waiting branches of a live run that have no bookmark to wake them, no stored wake, and have not
    /// been written since <paramref name="cutoffUtc"/>: branches stranded by a park that never wrote its
    /// bookmark. Defaulted so the test doubles that never sweep need no stub.
    /// </summary>
    Task<IReadOnlyCollection<BranchRow>> GetWaitingWithoutBookmarkAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
        => Task.FromResult<IReadOnlyCollection<BranchRow>>(Array.Empty<BranchRow>());

    /// <summary>
    /// Waiting branches of a live run whose bookmark a claim deleted to wake them, with the wake stored
    /// on the branch by that claim, paged by Id after <paramref name="afterId"/>. A new leader resumes
    /// these: the previous one may have stopped before dispatching them. Defaulted so the test doubles
    /// that never recover need no stub.
    /// </summary>
    Task<IReadOnlyCollection<WokenBranch>> GetWokenAsync(long afterId, int batchSize, CancellationToken ct)
        => Task.FromResult<IReadOnlyCollection<WokenBranch>>(Array.Empty<WokenBranch>());

    /// <summary>
    /// Waiting branches of a live run whose wake a claim stored and that have not been written since
    /// <paramref name="cutoffUtc"/>: woken, but not yet run, longer than a dispatch takes. Defaulted so the
    /// test doubles that never sweep need no stub.
    /// </summary>
    Task<IReadOnlyCollection<WokenBranch>> GetWokenNotWrittenSinceAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct)
        => Task.FromResult<IReadOnlyCollection<WokenBranch>>(Array.Empty<WokenBranch>());

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
    /// Ends the branch with <paramref name="toStatus"/> only if it is still live (Active or Waiting),
    /// and says whether it did. Two loops can reach the end of one branch - a cancel
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

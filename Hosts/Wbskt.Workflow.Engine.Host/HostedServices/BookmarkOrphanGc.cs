using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

/// <summary>
/// Reconciles bookmarks with the branches they wake, in both directions: deletes bookmarks whose run is
/// over, fails Waiting branches that have no bookmark left to wake them, dispatches woken branches whose
/// dispatch was lost, and wakes sub-workflow steps still parked on a child run that has already ended.
/// </summary>
public sealed class BookmarkOrphanGc(
    TimeProvider clock,
    ILeadershipGate leadership,
    IServiceScopeFactory scopeFactory,
    ILogger<BookmarkOrphanGc> logger,
    IOptions<WorkflowEngineOptions> options) : LeaderPeriodicWorker(leadership, scopeFactory, logger, clock)
{
    private const int StrandedBatchSize = 100;
    private const int RedispatchAgainAfterThresholds = 6;
    private readonly TimeSpan _strandedThreshold = options.Value.StrandedBranchThreshold;

    // Branches seen stranded on the previous pass, by id, with the row version they had then. The grace
    // period covers a park mid-way between its two writes; acting only on a branch seen, unchanged, on
    // two passes in a row leaves a whole interval more. Touched by one tick at a time, as are the two below.
    private Dictionary<long, byte[]> _suspects = new();

    // The same for woken branches still waiting for their loop: the claim stamps UpdatedAt, so the grace
    // period covers a wake that runs promptly, and the second sighting one queued behind a backlog.
    private Dictionary<long, byte[]> _wokenSuspects = new();

    // Woken branches this instance has dispatched, with the row version they had and when. The dispatch
    // queue is in memory, so one it holds is not lost while this instance leads, and dispatching the same
    // unchanged branch again soon would only run it twice behind a backlog. Only one still unchanged
    // long after (a loop that threw before writing it) is dispatched again.
    private Dictionary<long, (byte[] Version, DateTime At)> _redispatched = new();

    protected override string Name => "Bookmark orphan GC";

    protected override TimeSpan Interval { get; } = options.Value.BookmarkOrphanGcInterval;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var bookmarkProvider = services.GetRequiredService<IBookmarkProvider>();
        int deletedCount = await bookmarkProvider.DeleteOrphansAsync(ct);
        if (deletedCount > 0)
        {
            Logger.LogInformation("Orphan GC removed {Count} orphaned bookmarks", deletedCount);
        }

        await FailStrandedBranchesAsync(services, ct);
        await DispatchLostWakesAsync(services, ct);
        await WakeParentsOfEndedChildrenAsync(services, ct);
    }

    /// <summary>
    /// A parent parked on a child that has ended waits for ever when the child's completion event was
    /// lost: the finalizer stopped between the terminal status and the hook, or the event came before the
    /// parent parked and its own check did not run. Raising it again is harmless when it was not lost:
    /// the first event to claim the bookmark wins and a later one matches nothing. It waits the stranded
    /// threshold after the child ended, so a completion still on its way is not raised twice for nothing.
    /// </summary>
    private async Task WakeParentsOfEndedChildrenAsync(IServiceProvider services, CancellationToken ct)
    {
        var runProvider = services.GetRequiredService<IRunProvider>();
        DateTime cutoffUtc = clock.GetUtcNow().UtcDateTime - _strandedThreshold;
        IReadOnlyCollection<RunRow> children = await runProvider.GetEndedChildrenAwaitedAsync(cutoffUtc, StrandedBatchSize, ct);
        if (children.Count == 0)
        {
            return;
        }

        var completionHook = services.GetRequiredService<ISubWorkflowCompletionHook>();
        int raised = 0;
        foreach (RunRow child in children)
        {
            try
            {
                await completionHook.OnRunCompletedAsync(child.RefId, child.Status, child.OutputJson, ct);
                raised++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError(ex, "Orphan GC could not re-raise the completion of child run {ChildRunId}", child.Id);
            }
        }

        if (raised > 0)
        {
            Logger.LogWarning("Orphan GC re-raised the completion of {Count} child runs a parent was still waiting on", raised);
        }
    }

    private async Task FailStrandedBranchesAsync(IServiceProvider services, CancellationToken ct)
    {
        var branchProvider = services.GetRequiredService<IBranchProvider>();
        DateTime cutoffUtc = clock.GetUtcNow().UtcDateTime - _strandedThreshold;
        IReadOnlyCollection<BranchRow> stranded = await branchProvider.GetWaitingWithoutBookmarkAsync(cutoffUtc, StrandedBatchSize, ct);

        var suspects = new Dictionary<long, byte[]>();
        IStrandedBranchFailer? failer = null;
        int failedCount = 0;
        foreach (BranchRow branch in SeenTwice(stranded, branch => branch.Id, branch => branch.RowVersion, _suspects, suspects))
        {
            try
            {
                failer ??= services.GetRequiredService<IStrandedBranchFailer>();
                if (await failer.FailAsync(branch, ct))
                {
                    failedCount++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One branch that cannot be failed must not keep the rest of the batch stranded. It is
                // still stranded, so it stays a suspect and is retried on the next pass.
                suspects[branch.Id] = branch.RowVersion;
                Logger.LogError(ex, "Orphan GC could not fail stranded branch {BranchId} of run {RunId}", branch.Id, branch.RunId);
            }
        }

        _suspects = suspects;
        if (failedCount > 0)
        {
            Logger.LogWarning("Orphan GC failed {Count} branches stranded Waiting with no bookmark", failedCount);
        }
    }

    /// <summary>
    /// A wake is claimed and stored on its branch before the branch is dispatched, and the dispatch can
    /// be lost while the leader keeps running: the scheduler's read of a claimed bookmark fails, or the
    /// dispatch throws after the claim. A new leader resumes such branches, but a leader that never stops
    /// left them Waiting for ever. The stored wake makes dispatching one again do what the lost dispatch
    /// would have, as a new leader does.
    /// </summary>
    private async Task DispatchLostWakesAsync(IServiceProvider services, CancellationToken ct)
    {
        var branchProvider = services.GetRequiredService<IBranchProvider>();
        DateTime cutoffUtc = clock.GetUtcNow().UtcDateTime - _strandedThreshold;
        IReadOnlyCollection<WokenBranch> woken = await branchProvider.GetWokenNotWrittenSinceAsync(cutoffUtc, StrandedBatchSize, ct);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        var suspects = new Dictionary<long, byte[]>();
        var redispatched = new Dictionary<long, (byte[] Version, DateTime At)>();
        foreach (WokenBranch branch in woken)
        {
            if (_redispatched.TryGetValue(branch.Id, out var sent)
                && sent.Version.AsSpan().SequenceEqual(branch.RowVersion)
                && now - sent.At < _strandedThreshold * RedispatchAgainAfterThresholds)
            {
                redispatched[branch.Id] = sent;
            }
        }

        IRunDispatcher? dispatcher = null;
        int dispatched = 0;
        foreach (WokenBranch branch in SeenTwice(woken.Where(branch => !redispatched.ContainsKey(branch.Id)), branch => branch.Id, branch => branch.RowVersion, _wokenSuspects, suspects))
        {
            dispatcher ??= services.GetRequiredService<IRunDispatcher>();
            await dispatcher.DispatchAsync(new BranchExecutionRequest(branch.RunId, branch.Id, BranchExecutionReason.Recovered), ct);
            redispatched[branch.Id] = (branch.RowVersion, now);
            dispatched++;
        }

        _wokenSuspects = suspects;
        _redispatched = redispatched;
        if (dispatched > 0)
        {
            Logger.LogWarning("Orphan GC dispatched {Count} woken branches whose dispatch was lost", dispatched);
        }
    }

    /// <summary>
    /// The branches also seen, unchanged, on the previous pass. Every other branch is recorded in
    /// <paramref name="next"/> as a suspect for the next pass.
    /// </summary>
    private static List<T> SeenTwice<T>(IEnumerable<T> branches, Func<T, long> idOf, Func<T, byte[]> versionOf, Dictionary<long, byte[]> previous, Dictionary<long, byte[]> next)
    {
        var confirmed = new List<T>();
        foreach (T branch in branches)
        {
            if (previous.TryGetValue(idOf(branch), out byte[]? seenVersion) && seenVersion.AsSpan().SequenceEqual(versionOf(branch)))
            {
                confirmed.Add(branch);
            }
            else
            {
                next[idOf(branch)] = versionOf(branch);
            }
        }

        return confirmed;
    }
}

using Microsoft.Extensions.Caching.Memory;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class RunCancellationService : IRunCancellationService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(10);
    private readonly IRunProvider _runProvider;
    private readonly IRunJournal _journal;
    private readonly IMemoryCache _memoryCache;
    private readonly IClock _clock;
    private readonly IBranchProvider _branchProvider;
    private readonly IBookmarkProvider _bookmarkProvider;
    private readonly IBranchTerminator _branchTerminator;

    // Singleton, not per-instance: this service is Scoped and the pump runs each branch in its own
    // scope, so a per-instance registry meant the branch loop watched one token source while every
    // canceller cancelled another. See RunCancellationTokenRegistry.
    private readonly RunCancellationTokenRegistry _tokenRegistry;

    public RunCancellationService(
        IRunProvider runProvider,
        IRunJournal journal,
        IMemoryCache memoryCache,
        IClock clock,
        IBranchProvider branchProvider,
        IBookmarkProvider bookmarkProvider,
        IBranchTerminator branchTerminator,
        RunCancellationTokenRegistry tokenRegistry)
    {
        _tokenRegistry = tokenRegistry;
        _runProvider = runProvider;
        _journal = journal;
        _memoryCache = memoryCache;
        _clock = clock;
        _branchProvider = branchProvider;
        _bookmarkProvider = bookmarkProvider;
        _branchTerminator = branchTerminator;
    }

    public CancellationToken GetToken(long runId)
    {
        return _tokenRegistry.GetToken(runId);
    }

    public void RemoveCts(long runId)
    {
        _tokenRegistry.Remove(runId);
    }

    // Cancellation is read through a short-lived cache, so the cancel overwrites the cached answer
    // rather than leaving IsCancellationRequestedAsync returning a stale false until the entry expires.
    private void MarkCancellationRequested(long runId)
    {
        _memoryCache.Set(CreateCacheKey(runId), true, CacheTtl);
        CancelCts(runId);
    }

    // Also called from RunRecoveryService, where a run found already in 'Failing' needs its token
    // cancelled without being moved to 'Cancelling'.
    public void CancelCts(long runId)
    {
        _tokenRegistry.Cancel(runId);
    }

    public async Task<bool> RequestCancellationAsync(long runId, string reason, CancellationToken ct)
    {
        // A cancel applies to any non-terminal run, not just a 'Running' one. A run whose first branch
        // already failed sits in 'Failing' while its siblings keep going - refusing to cancel that was
        // silent and inexplicable from the outside. (A parked run is still 'Running'; only its branch is
        // 'Waiting'.) Single status write (2.3): the reason/timestamp ride along with the transition
        // itself, so there's no follow-up UpdateStatusAsync call.
        bool transitioned = await _runProvider.TransitionStatusAsync(runId, RunStatus.Running, RunStatus.Cancelling, _clock.UtcNow, reason, ct)
            || await _runProvider.TransitionStatusAsync(runId, RunStatus.Failing, RunStatus.Cancelling, _clock.UtcNow, reason, ct);

        if (!transitioned)
        {
            // Not transitioning means either "already Cancelling" or "terminal". The first still gets the
            // cleanup below: a redelivered cancel command, or one sent while the run was already
            // cancelling, must not leave bookmarks or a waiting branch behind.
            string currentStatus = (await _runProvider.GetByIdAsync(runId, ct)).Status;
            if (!string.Equals(currentStatus, RunStatus.Cancelling, StringComparison.Ordinal))
            {
                return false;
            }
        }
        else
        {
            await _journal.AppendAsync(
                new RunJournalEntry(checked((int)runId), HistoryEventKind.RunCancellationRequested) { Payload = new { Reason = reason } },
                ct);
        }

        // Flip the local view before the cleanup, so anything checking mid-way sees the cancel.
        MarkCancellationRequested(runId);

        // Mass-delete bookmarks for this run. Every step below is idempotent, which is what lets the
        // already-Cancelling path above fall through to it safely.
        await _bookmarkProvider.DeleteAllByRunIdAsync(checked((int)runId), ct);

        int cancelledBranches = await _branchProvider.CancelWaitingBranchesAsync(checked((int)runId), ct);

        // A run with nothing left running has no branch loop to carry it to a terminal status, so
        // without this it would sit in 'Cancelling' until the 30-minute reaper swept it. Releasing zero
        // branches, as on a repeated cancel, still finalizes a run that is already idle.
        await ReleaseCancelledBranchesAsync(runId, cancelledBranches, ct);

        return true;
    }

    private async Task ReleaseCancelledBranchesAsync(long runId, int cancelledBranches, CancellationToken ct)
    {
        // Through the terminator, not a bare SetTerminalAsync (2.3): that would skip the pending-trigger
        // drain, the run-completed publish and the sub-workflow completion hook, stranding parents.
        try
        {
            await _branchTerminator.ReleaseAsync(checked((int)runId), cancelledBranches, ct);
        }
        catch (Exception)
        {
            // The cancel itself succeeded; a run left in 'Cancelling' is picked up by the branch loop
            // when its last branch unwinds, or by the reaper. Failing the caller's cancel over this
            // would report "not cancelled" for a run that is, in fact, cancelling.
        }
    }

    public async Task<bool> IsCancellationRequestedAsync(long runId, CancellationToken ct)
    {
        if (_memoryCache.TryGetValue(CreateCacheKey(runId), out bool cached))
        {
            return cached;
        }

        string status = (await _runProvider.GetByIdAsync(runId, ct)).Status;
        bool isCancellationRequested = string.Equals(status, RunStatus.Cancelling, StringComparison.Ordinal)
            || string.Equals(status, RunStatus.Cancelled, StringComparison.Ordinal);
        _memoryCache.Set(CreateCacheKey(runId), isCancellationRequested, CacheTtl);
        return isCancellationRequested;
    }

    private static string CreateCacheKey(long runId)
    {
        return $"run-cancellation:{runId}";
    }
}

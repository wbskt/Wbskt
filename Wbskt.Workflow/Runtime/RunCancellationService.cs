using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Wbskt.EventBus.Abstractions;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class RunCancellationService : IRunCancellationService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(10);
    private readonly IRunProvider _runProvider;
    private readonly IHistoryEventProvider _historyEventProvider;
    private readonly IMemoryCache _memoryCache;
    private readonly IClock _clock;
    private readonly IBranchProvider? _branchProvider;
    private readonly IBookmarkProvider? _bookmarkProvider;
    private readonly IRunCountersProvider? _runCountersProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly IEventBus? _eventBus;

    // Singleton, not per-instance: this service is Scoped and the pump runs each branch in its own
    // scope, so a per-instance registry meant the branch loop watched one token source while every
    // canceller cancelled another. See RunCancellationTokenRegistry.
    private readonly RunCancellationTokenRegistry _tokenRegistry;

    public RunCancellationService(
        IRunProvider runProvider,
        IHistoryEventProvider historyEventProvider,
        IMemoryCache memoryCache,
        IClock clock,
        IServiceProvider serviceProvider,
        IBranchProvider? branchProvider = null,
        IBookmarkProvider? bookmarkProvider = null,
        IRunCountersProvider? runCountersProvider = null,
        IEventBus? eventBus = null,
        RunCancellationTokenRegistry? tokenRegistry = null)
    {
        // Optional so the many hand-built test instances need no extra argument; the engine always
        // supplies the singleton, and a hand-built instance gets its own private registry, which is
        // exactly the isolation a test wants.
        _tokenRegistry = tokenRegistry ?? new RunCancellationTokenRegistry();
        _runProvider = runProvider;
        _historyEventProvider = historyEventProvider;
        _memoryCache = memoryCache;
        _clock = clock;
        _branchProvider = branchProvider;
        _bookmarkProvider = bookmarkProvider;
        _runCountersProvider = runCountersProvider;
        _serviceProvider = serviceProvider;
        _eventBus = eventBus;
    }

    public CancellationToken GetToken(long runId)
    {
        return _tokenRegistry.GetToken(runId);
    }

    public void RemoveCts(long runId)
    {
        _tokenRegistry.Remove(runId);
    }

    /// <summary>
    /// Flips this host's view of the run to "cancelling" without touching the database. Cancellation is
    /// read through a short-lived per-host cache, so a host that learns of a cancel from the event bus
    /// rather than by issuing it must call this or <see cref="IsCancellationRequestedAsync"/> keeps
    /// returning a stale <c>false</c> until the entry expires.
    /// </summary>
    public void MarkCancellationRequested(long runId)
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
        bool transitioned = await _runProvider.TransitionStatusAsync(runId, "Running", "Cancelling", _clock.UtcNow, reason, ct)
            || await _runProvider.TransitionStatusAsync(runId, "Failing", "Cancelling", _clock.UtcNow, reason, ct);

        if (!transitioned)
        {
            // Not transitioning means either "already Cancelling" or "terminal". The first still needs
            // the local work below - that is the case where another host issued the cancel and this one
            // is picking it up off the bus, and it owns the bookmarks, the branches and the finalizer.
            string currentStatus = (await _runProvider.GetByIdAsync(runId, ct)).Status;
            if (!string.Equals(currentStatus, "Cancelling", StringComparison.Ordinal))
            {
                return false;
            }
        }
        else
        {
            await _historyEventProvider.InsertBatchAsync(
            [
                new HistoryEventRow
                {
                    HistoryEventId = 0,
                    RunId = checked((int)runId),
                    BranchRefId = null,
                    NodeId = null,
                    EventKind = HistoryEventKind.RunCancellationRequested,
                    Severity = HistoryEventKind.SeverityFor(HistoryEventKind.RunCancellationRequested),
                    PayloadJson = JsonSerializer.Serialize(new { Reason = reason }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                    Timestamp = _clock.UtcNow
                }
            ], ct);
        }

        // Flip the local view before the cleanup, so anything checking mid-way sees the cancel.
        MarkCancellationRequested(runId);

        // Mass-delete bookmarks for this run. Every step below is idempotent, which is what lets the
        // already-Cancelling path above fall through to it safely.
        if (_bookmarkProvider is not null)
        {
            await _bookmarkProvider.DeleteAllByRunIdAsync(checked((int)runId), ct);
        }

        // Cancel waiting branches
        int? activeBranchCount = null;
        if (_branchProvider is not null)
        {
            int cancelledBranches = await _branchProvider.CancelWaitingBranchesAsync(checked((int)runId), ct);
            if (cancelledBranches > 0 && _runCountersProvider is not null)
            {
                activeBranchCount = await _runCountersProvider.IncrementActiveBranchesAsync(checked((int)runId), -cancelledBranches, ct);
            }
        }

        // A run with nothing left running has no branch loop to carry it to a terminal status, so
        // without this it would sit in 'Cancelling' until the 30-minute reaper swept it. The count is
        // re-read when this call cancelled no branches itself, which is the normal shape on the host
        // that picked the cancel up off the bus after another host had already done the branch work.
        await FinalizeIfIdleAsync(runId, activeBranchCount, ct);

        // Only announce a transition this call actually made. Republishing on the already-Cancelling
        // path would bounce the event between hosts forever, since each one consumes what the other sends.
        if (transitioned && _eventBus is not null)
        {
            await _eventBus.PublishAsync(new Events.Workflow.WorkflowRunCancellationRequestedEvent(runId, reason), ct);
        }

        return true;
    }

    private async Task FinalizeIfIdleAsync(long runId, int? knownActiveBranchCount, CancellationToken ct)
    {
        // No bare SetTerminalAsync fallback here (2.3): a caller without a finalizer registered (a test
        // host; the management host no longer cancels at all) relies on the engine host - which always
        // has one - to finalize. A bare
        // SetTerminalAsync would skip the pending-trigger drain, run-completed publish, and sub-workflow
        // completion hook, stranding parents.
        var runFinalizer = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<IRunFinalizer>(_serviceProvider);
        if (runFinalizer is null || _runCountersProvider is null)
        {
            return;
        }

        try
        {
            int activeBranchCount = knownActiveBranchCount
                ?? (await _runCountersProvider.GetByRunIdAsync(checked((int)runId), ct)).ActiveBranchCount;
            if (activeBranchCount <= 0)
            {
                await runFinalizer.FinalizeAsync(runId, ct);
            }
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
        bool isCancellationRequested = string.Equals(status, "Cancelling", StringComparison.Ordinal)
            || string.Equals(status, "Cancelled", StringComparison.Ordinal);
        _memoryCache.Set(CreateCacheKey(runId), isCancellationRequested, CacheTtl);
        return isCancellationRequested;
    }

    private static string CreateCacheKey(long runId)
    {
        return $"run-cancellation:{runId}";
    }
}

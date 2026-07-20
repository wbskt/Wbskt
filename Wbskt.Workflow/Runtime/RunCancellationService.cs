using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _ctsRegistry = new();

    public RunCancellationService(
        IRunProvider runProvider,
        IHistoryEventProvider historyEventProvider,
        IMemoryCache memoryCache,
        IClock clock,
        IServiceProvider serviceProvider,
        IBranchProvider? branchProvider = null,
        IBookmarkProvider? bookmarkProvider = null,
        IRunCountersProvider? runCountersProvider = null,
        IEventBus? eventBus = null)
    {
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
        var cts = _ctsRegistry.GetOrAdd(runId, _ => new CancellationTokenSource());
        return cts.Token;
    }

    public void RemoveCts(long runId)
    {
        if (_ctsRegistry.TryRemove(runId, out var cts))
        {
            try
            {
                cts.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    // [RJ]: TODO: why this needs to be also called from RunRecoveryService?
    // [RJ]: if we can avoid that call, we can make this private.
    public void CancelCts(long runId)
    {
        var cts = _ctsRegistry.GetOrAdd(runId, _ => {
            var newCts = new CancellationTokenSource();
            newCts.Cancel();
            return newCts;
        });
        if (!cts.IsCancellationRequested)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    // [RJ]: TODO: cancel internally uses a cache but it lives in WMH and WEH separately.
    // [RJ]: TODO: cancellation must be passed to WEH from WMH through events
    public async Task<bool> RequestCancellationAsync(long runId, string reason, CancellationToken ct)
    {
        // [RJ]: this will transition only if the run currently is in "Running" what about requesting cancellation for runs that are waiting/bookmarked.
        // Single status write (2.3): the reason/timestamp ride along with the transition itself,
        // so there's no follow-up UpdateStatusAsync call (and no extra GetByIdAsync round trip).
        bool transitioned = await _runProvider.TransitionStatusAsync(runId, "Running", "Cancelling", _clock.UtcNow, reason, ct);
        if (!transitioned)
        {
            return false;
        }

        await _historyEventProvider.InsertBatchAsync(
        [
            new HistoryEventRow
            {
                HistoryEventId = 0,
                RunId = checked((int)runId),
                BranchRefId = null,
                NodeId = null,
                EventKind = "RunCancellationRequested",
                Severity = "Info",
                PayloadJson = JsonSerializer.Serialize(new { Reason = reason }, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Timestamp = _clock.UtcNow
            }
        ], ct);

        // Cancel CTS
        CancelCts(runId);

        // Mass-delete bookmarks for this run
        // [RJ]: TODO: what about the cancellation from the RunRecoveryService? dont we need to delete bookmarks of those runs too?
        // [RJ]: since we are calling "CancelCts" from the RunRecoveryService.
        if (_bookmarkProvider is not null)
        {
            await _bookmarkProvider.DeleteAllByRunIdAsync(checked((int)runId), ct);
        }

        // Cancel waiting branches
        if (_branchProvider is not null)
        {
            int cancelledBranches = await _branchProvider.CancelWaitingBranchesAsync(checked((int)runId), ct);
            if (cancelledBranches > 0 && _runCountersProvider is not null)
            {
                int newActiveCount = await _runCountersProvider.IncrementActiveBranchesAsync(checked((int)runId), -cancelledBranches, ct);
                if (newActiveCount == 0)
                {
                    // No bare SetTerminalAsync fallback here (2.3): a caller without a finalizer
                    // registered (e.g. the management host) relies on the engine host - which always
                    // has one - to finalize. A bare SetTerminalAsync would skip the pending-trigger
                    // drain, run-completed publish, and sub-workflow completion hook, stranding parents.
                    var runFinalizer = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<IRunFinalizer>(_serviceProvider);
                    if (runFinalizer is not null)
                    {
                        await runFinalizer.FinalizeAsync(runId, ct);
                    }
                }
            }
        }

        if (_eventBus is not null)
        {
            await _eventBus.PublishAsync(new Events.Workflow.WorkflowRunCancellationRequestedEvent(runId, reason), ct);
        }

        // [RJ]: the memory cache stores if the run is canceled or not for the past 10 seconds
        // [RJ]: IsCancellationRequestedAsync re-caches it from the DB if it's a cache miss
        _memoryCache.Set(CreateCacheKey(runId), true, CacheTtl);
        return true;
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

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
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
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _ctsRegistry = new();

    public RunCancellationService(
        IRunProvider runProvider,
        IHistoryEventProvider historyEventProvider,
        IMemoryCache memoryCache,
        IClock clock,
        IBranchProvider? branchProvider = null,
        IBookmarkProvider? bookmarkProvider = null)
    {
        _runProvider = runProvider;
        _historyEventProvider = historyEventProvider;
        _memoryCache = memoryCache;
        _clock = clock;
        _branchProvider = branchProvider;
        _bookmarkProvider = bookmarkProvider;
    }

    public CancellationToken GetToken(long runId)
    {
        var cts = _ctsRegistry.GetOrAdd(runId, _ => { return new CancellationTokenSource(); });
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

    // TODO: cancel internally uses a cache but it lives in WMH and WEH separately.
    // TODO: cancellation must be passed to WEH from WMH through events
    public async Task<bool> RequestCancellationAsync(long runId, string reason, CancellationToken ct)
    {
        bool transitioned = await _runProvider.TransitionStatusAsync(runId, "Running", "Cancelling", ct);
        if (!transitioned)
        {
            return false;
        }

        RunRow run = await _runProvider.GetByIdAsync(runId, ct);
        await _runProvider.UpdateStatusAsync(run.RefId, "Cancelling", null, _clock.UtcNow, reason, ct);
        await _historyEventProvider.InsertBatchAsync(
        [
            new HistoryEventRow
            {
                HistoryEventId = 0,
                RunId = run.Id,
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
        if (_bookmarkProvider is not null)
        {
            await _bookmarkProvider.DeleteAllByRunIdAsync(checked((int)runId), ct);
        }

        // Cancel waiting branches
        if (_branchProvider is not null)
        {
            await _branchProvider.CancelWaitingBranchesAsync(checked((int)runId), ct);
        }

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

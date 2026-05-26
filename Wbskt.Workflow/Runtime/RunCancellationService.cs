using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

public sealed class RunCancellationService : IRunCancellationService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(10);
    private readonly IRunProvider _runProvider;
    private readonly IHistoryEventProvider _historyEventProvider;
    private readonly IMemoryCache _memoryCache;
    private readonly IClock _clock;

    public RunCancellationService(
        IRunProvider runProvider,
        IHistoryEventProvider historyEventProvider,
        IMemoryCache memoryCache,
        IClock clock)
    {
        _runProvider = runProvider;
        _historyEventProvider = historyEventProvider;
        _memoryCache = memoryCache;
        _clock = clock;
    }

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

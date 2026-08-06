using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Providers;

public interface IHistoryEventProvider
{
    Task InsertBatchAsync(IReadOnlyCollection<HistoryEventRow> events, CancellationToken ct);
    Task<IReadOnlyCollection<HistoryEventRow>> GetByRunIdAsync(int runId, long afterEventId, int pageSize, CancellationToken ct);
    /// <summary>
    /// Two-window retention. Routine entries go after <paramref name="cutoffUtc"/>; Warn/Error
    /// entries are kept until <paramref name="elevatedCutoffUtc"/> — far longer, but not forever.
    /// A null elevated cutoff keeps them indefinitely.
    /// </summary>
    Task<int> DeleteForRetiredRunsAsync(DateTime cutoffUtc, int batchSize, DateTime? elevatedCutoffUtc, CancellationToken ct);
}

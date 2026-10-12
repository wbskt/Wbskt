using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class RunJournal(IHistoryEventProvider historyEventProvider, TimeProvider clock) : IRunJournal
{
    public Task AppendAsync(RunJournalEntry entry, CancellationToken ct)
    {
        return historyEventProvider.InsertBatchAsync(
        [
            new HistoryEventRow
            {
                HistoryEventId = 0,
                RunId = entry.RunId,
                BranchRefId = entry.BranchRefId,
                NodeId = entry.NodeId,
                EventKind = entry.EventKind,
                Severity = entry.Severity ?? HistoryEventKind.SeverityFor(entry.EventKind),
                PayloadJson = entry.Payload is null ? null : JsonSerializer.Serialize(entry.Payload, entry.Payload.GetType(), EngineJson.Options),
                Timestamp = clock.GetUtcNow().UtcDateTime
            }
        ], ct);
    }

    public Task<IReadOnlyCollection<HistoryEventRow>> ReadAllAsync(int runId, CancellationToken ct)
    {
        return historyEventProvider.GetByRunIdAsync(runId, 0, int.MaxValue, ct);
    }
}

using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class RunJournal(IHistoryEventProvider historyEventProvider, TimeProvider clock) : IRunJournal
{
    public Task AppendAsync(RunJournalEntry entry, CancellationToken ct)
    {
        return AppendAsync([entry], ct);
    }

    public Task AppendAsync(IReadOnlyCollection<RunJournalEntry> entries, CancellationToken ct)
    {
        if (entries.Count == 0)
        {
            return Task.CompletedTask;
        }

        DateTime now = clock.GetUtcNow().UtcDateTime;
        return historyEventProvider.InsertBatchAsync(entries.Select(entry => ToRow(entry, now)).ToList(), ct);
    }

    private static HistoryEventRow ToRow(RunJournalEntry entry, DateTime timestamp) => new()
    {
        HistoryEventId = 0,
        RunId = entry.RunId,
        BranchRefId = entry.BranchRefId,
        NodeId = entry.NodeId,
        EventKind = entry.EventKind,
        Severity = entry.Severity ?? HistoryEventKind.SeverityFor(entry.EventKind),
        PayloadJson = entry.Payload is null ? null : JsonSerializer.Serialize(entry.Payload, entry.Payload.GetType(), EngineJson.Options),
        Timestamp = timestamp
    };

    public Task<IReadOnlyCollection<HistoryEventRow>> ReadAllAsync(int runId, CancellationToken ct)
    {
        return historyEventProvider.GetByRunIdAsync(runId, 0, int.MaxValue, ct);
    }
}

using Wbskt.Workflow.Abstraction.Entities;

namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>
/// The one writer of a run's history. Callers say what happened; the journal stamps the time, picks
/// the severity (<see cref="HistoryEventKind.SeverityFor"/>) and serializes the payload, so those are
/// decided once rather than by every class that records an event.
/// </summary>
public interface IRunJournal
{
    Task AppendAsync(RunJournalEntry entry, CancellationToken ct);

    /// <summary>Everything recorded for a run, oldest first.</summary>
    Task<IReadOnlyCollection<HistoryEventRow>> ReadAllAsync(int runId, CancellationToken ct);
}

/// <param name="RunId">The run the entry belongs to.</param>
/// <param name="EventKind">One of <see cref="HistoryEventKind"/>.</param>
public sealed record RunJournalEntry(int RunId, string EventKind)
{
    public Guid? BranchRefId { get; init; }

    public Guid? NodeId { get; init; }

    /// <summary>Serialized as JSON (web defaults). Pass the object, not a JSON string.</summary>
    public object? Payload { get; init; }

    /// <summary>Overrides the kind's default severity, for kinds whose severity depends on the payload.</summary>
    public string? Severity { get; init; }
}

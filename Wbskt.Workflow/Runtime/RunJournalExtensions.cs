using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// The shape the branch loop and its result handlers record in: one event on one branch, at a node or
/// at none.
/// </summary>
internal static class RunJournalExtensions
{
    public static Task AppendBranchEventAsync(this IRunJournal journal, int runId, Guid branchRefId, Guid? nodeId, string eventKind, CancellationToken ct)
    {
        return journal.AppendBranchEventAsync(runId, branchRefId, nodeId, eventKind, null, ct);
    }

    public static Task AppendBranchEventAsync(this IRunJournal journal, int runId, Guid branchRefId, Guid? nodeId, string eventKind, object? payload, CancellationToken ct)
    {
        return journal.AppendAsync(new RunJournalEntry(runId, eventKind) { BranchRefId = branchRefId, NodeId = nodeId, Payload = payload }, ct);
    }
}

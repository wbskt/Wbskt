using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// The two ordinary ends of a branch, shared by the branch loop (a cancelled run, a pointer past the
/// last node) and the result handlers (a port that leads nowhere, a terminal result): the branch's
/// status, then its history entry, then its release through <see cref="IBranchTerminator"/>.
/// </summary>
internal sealed class BranchEndings(IBranchProvider branchProvider, IRunJournal journal, IBranchTerminator branchTerminator)
{
    public async Task CompleteAsync(int runId, long branchId, Guid branchRefId, CancellationToken ct)
    {
        await branchProvider.SetCompletedAsync(branchId, ct);
        await journal.AppendBranchEventAsync(runId, branchRefId, null, HistoryEventKind.BranchCompleted, ct);
        await branchTerminator.ReleaseAsync(runId, 1, ct);
    }

    public async Task CancelAsync(int runId, long branchId, BranchRow branchRow, CancellationToken ct)
    {
        await branchProvider.UpdatePointerAsync(branchId, branchRow.NodeId, BranchStatus.Cancelled, branchRow.LocalJson, branchRow.LastOutputJson, ct);
        await journal.AppendBranchEventAsync(runId, branchRow.RefId, null, HistoryEventKind.BranchCancelled, ct);
        await branchTerminator.ReleaseAsync(runId, 1, ct);
    }
}

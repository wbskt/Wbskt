using Microsoft.Extensions.Logging;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// The two ordinary ends of a branch, shared by the branch loop (a cancelled run, a pointer past the
/// last node) and the result handlers (a port that leads nowhere, a terminal result): the branch's
/// status, then its history entry, then its release through <see cref="IBranchTerminator"/>.
/// </summary>
/// <remarks>
/// The status write is a compare-and-set (<see cref="IBranchProvider.EndIfLiveAsync"/>), and the
/// history entry and the release follow only when it took. A branch can be ended twice - a cancel
/// racing a bookmark wake, a recovery requeue racing a slow node on the old leader - and the second
/// end used to overwrite the first and release the branch again. The decrement is clamped at zero,
/// which hid it, but with three or more live branches the extra release let the run reach zero active
/// branches one branch early and finalise while a branch was still running.
/// </remarks>
internal sealed class BranchEndings(IBranchProvider branchProvider, IRunJournal journal, IBranchTerminator branchTerminator, ILogger<BranchEndings>? logger = null)
{
    public async Task CompleteAsync(int runId, long branchId, Guid branchRefId, CancellationToken ct)
    {
        if (!await branchProvider.EndIfLiveAsync(branchId, BranchStatus.Completed, null, ct))
        {
            LogAlreadyEnded(runId, branchId, BranchStatus.Completed);
            return;
        }

        await journal.AppendBranchEventAsync(runId, branchRefId, null, HistoryEventKind.BranchCompleted, ct);
        await branchTerminator.ReleaseAsync(runId, 1, ct);
    }

    public async Task CancelAsync(int runId, long branchId, BranchRow branchRow, CancellationToken ct)
    {
        if (!await branchProvider.EndIfLiveAsync(branchId, BranchStatus.Cancelled, branchRow.LastOutputJson, ct))
        {
            LogAlreadyEnded(runId, branchId, BranchStatus.Cancelled);
            return;
        }

        await journal.AppendBranchEventAsync(runId, branchRow.RefId, null, HistoryEventKind.BranchCancelled, ct);
        await branchTerminator.ReleaseAsync(runId, 1, ct);
    }

    private void LogAlreadyEnded(int runId, long branchId, string attemptedStatus)
    {
        logger?.LogWarning(
            "Branch {BranchId} on run {RunId} was already ended when this loop tried to end it {Status}; it was ended elsewhere, so it is not released again.",
            branchId,
            runId,
            attemptedStatus);
    }
}

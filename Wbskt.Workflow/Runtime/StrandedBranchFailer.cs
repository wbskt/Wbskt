using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime.BranchResults;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// Fails a branch stranded Waiting with no bookmark. Parking is two writes - the branch to Waiting,
/// then its bookmark - and a crash or a throw between them left a branch nothing would ever wake: crash
/// recovery only re-dispatches Active branches, the reaper treats Waiting as live work, and the branch
/// kept its run's ActiveBranchCount above zero, so the run never finalized.
///
/// The loss is handed to the same <see cref="NodeExecutionResult.Fail"/> handler a failing node goes
/// through, so the node's onFailure, the workflow's FailFast setting, and the Join
/// contribution of a cohort branch all apply as they would to any other failure of that node.
/// </summary>
internal sealed class StrandedBranchFailer(
    IBranchProvider branchProvider,
    IBookmarkProvider bookmarkProvider,
    IRunProvider runProvider,
    IWorkflowDefinitionCache workflowDefinitionCache,
    IBranchResultDispatcher resultDispatcher,
    IRunCancellationService runCancellationService,
    IRunDispatcher runDispatcher,
    IRunJournal journal,
    IBranchTerminator branchTerminator,
    ILogger<StrandedBranchFailer>? logger = null) : IStrandedBranchFailer
{
    internal const string ErrorCode = "BOOKMARK_LOST";

    public async Task<bool> FailAsync(BranchRow branch, CancellationToken ct)
    {
        // The sweep's read is a snapshot. Re-read the branch and its bookmarks, so a branch that was
        // woken, moved on, or finally got its bookmark since then is left alone. The row version is the
        // branch's visit token, so an unchanged one means nothing has written the branch since.
        BranchRow current = await branchProvider.GetByIdAsync(branch.Id, ct);
        if (!string.Equals(current.Status, BranchStatus.Waiting, StringComparison.Ordinal)
            || !current.RowVersion.AsSpan().SequenceEqual(branch.RowVersion))
        {
            return false;
        }

        IReadOnlyCollection<BookmarkRow> bookmarks = await bookmarkProvider.GetAllByRunIdAsync(current.RunId, ct);
        if (bookmarks.Any(bookmark => bookmark.BranchRefId == current.RefId))
        {
            return false;
        }

        RunRow run = await runProvider.GetByIdAsync(current.RunId, ct);
        if (string.Equals(run.Status, RunStatus.Cancelling, StringComparison.Ordinal))
        {
            // The run's stop cancels every Waiting branch; one still here means that stop was cut
            // short. Re-applying it is idempotent and ends this branch as Cancelled, which is what the
            // run asked for, rather than as a failure that would read as a second cause.
            logger?.LogWarning("Branch {BranchId} of cancelling run {RunId} was stranded with no bookmark; re-applying the cancellation.", current.Id, run.Id);
            return await runCancellationService.RequestCancellationAsync(run.Id, run.CancellationReason ?? "Cancellation re-applied for a stranded branch", ct);
        }

        if (string.Equals(run.Status, RunStatus.Failing, StringComparison.Ordinal))
        {
            logger?.LogWarning("Branch {BranchId} of failing run {RunId} was stranded with no bookmark; re-applying the run failure.", current.Id, run.Id);
            return await runCancellationService.FailRunAsync(run.Id, "Run failure re-applied for a stranded branch", ct);
        }

        if (!string.Equals(run.Status, RunStatus.Running, StringComparison.Ordinal))
        {
            return false;
        }

        var fail = new NodeExecutionResult.Fail(
            ErrorCode,
            $"Branch {current.RefId} was parked at node {current.NodeId} with no bookmark to wake it, so its wait was lost.",
            false,
            null);
        logger?.LogWarning("Branch {BranchId} of run {RunId} was stranded Waiting at node {NodeId} with no bookmark; failing it with {ErrorCode}.", current.Id, run.Id, current.NodeId, ErrorCode);

        // NodeFailed first, as the branch loop records it for any failing node, so the trace says which
        // wait was lost before it says how the failure was handled.
        await journal.AppendBranchEventAsync(run.Id, current.RefId, current.NodeId, HistoryEventKind.NodeFailed, new { fail.ErrorCode, fail.Message }, ct);

        WorkflowDefinition definition = await workflowDefinitionCache.GetAsync(run.WorkflowDefinitionId, ct);
        BaseNode? node = definition.Nodes.SingleOrDefault(candidate => candidate.NodeId == current.NodeId);
        if (node is null)
        {
            // No node to read an onFailure from (the branch loop completes a branch in this state);
            // end the branch Failed and let the finalizer decide the run's outcome.
            string errorJson = JsonSerializer.Serialize(new { fail.ErrorCode, fail.Message }, EngineJson.Options);
            await branchProvider.SetFailedAsync(current.Id, errorJson, ct);
            await journal.AppendBranchEventAsync(run.Id, current.RefId, current.NodeId, HistoryEventKind.BranchFailed, new { fail.ErrorCode, fail.Message }, ct);
            await branchTerminator.ReleaseAsync(run.Id, 1, ct);
            return true;
        }

        BranchResultContext context = new(run.Id, current.Id, run, current, definition, node, BranchLoop.BuildBranchContext(current, run, definition.WorkspaceId));
        BranchResultOutcome outcome = await resultDispatcher.DispatchAsync(fail, context, ct);
        if (outcome.Branch is { } moved)
        {
            // The node's onFailure routed the failure on (an error port, or a jump), so the branch is
            // Active at its next node with no loop running it. Hand it to one, as a wake would.
            await runDispatcher.DispatchAsync(new BranchExecutionRequest(run.Id, moved.Id, BranchExecutionReason.BookmarkResumed), ct);
        }

        return true;
    }
}

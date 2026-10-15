using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Controls;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// The vote a cohort branch casts when it ends without reaching its Join - through an End node or a
/// port with no edge. Only arriving at the Join, or failing, used to count, so a Mode=All cohort with
/// a member that simply ended never converged: the other members ended as "not the last", the run
/// finalized Succeeded, and everything after the Join was skipped without a trace.
///
/// The aggregator decides what a decline means. Mode=All cannot converge without the member, so the
/// join is abandoned and the run fails with <see cref="ErrorCode"/>; Any and Quorum carry on without
/// it until the remaining members can no longer meet them. Either way the branch's walk-away is
/// journaled, so the console shows which branch left its join.
/// </summary>
internal sealed class JoinDeclines(
    IJoinAggregatorProvider joinAggregatorProvider,
    IRunCancellationService runCancellationService,
    IRunJournal journal)
{
    public const string ErrorCode = "JOIN_BRANCH_ENDED";

    /// <summary>
    /// Declines the Join named by <paramref name="localState"/>'s token, if it carries one. Called
    /// before the branch is completed and released, so a run failed here is already Failing when
    /// the branch's release finalizes it.
    /// </summary>
    public async Task DeclineAsync(int runId, BranchRow branch, Guid nodeId, IReadOnlyDictionary<string, JsonElement> localState, CancellationToken ct)
    {
        if (!localState.TryGetValue(JoinTokens.TokenKey, out JsonElement tokenElement)
            || tokenElement.ValueKind != JsonValueKind.String
            || !Guid.TryParse(tokenElement.GetString(), out Guid joinToken))
        {
            // Not part of a Fork/ParallelForEach cohort, or past its outermost Join.
            return;
        }

        JoinContributionResult result = await joinAggregatorProvider.ContributeAsync(joinToken, branch.Id, "declined", ct);

        await journal.AppendBranchEventAsync(
            runId,
            branch.RefId,
            nodeId,
            HistoryEventKind.JoinAbandoned,
            new { joinNodeId = result.JoinNodeId, joinFailed = result.Abandoned, errorCode = result.Abandoned ? ErrorCode : null },
            ct);

        if (result.Abandoned)
        {
            await runCancellationService.FailRunAsync(
                runId,
                $"Join {result.JoinNodeId} cannot converge ({ErrorCode}): branch {branch.RefId} ended at node {nodeId} without reaching it",
                ct);
        }
    }
}

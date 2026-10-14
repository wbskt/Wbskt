using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Models.Nodes.Actions;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime.BranchResults;

/// <summary>
/// <see cref="NodeExecutionResult.Fail"/>: applies the node's OnFailure. A failure it turns into a
/// <see cref="NodeExecutionResult.Continue"/> is handled as one. Otherwise the branch fails (after
/// compensating, or failing the run, as configured), an out-of-credits failure cancels the run, the
/// branch's end is recorded, a cohort branch contributes "failed" to its Join, and the branch is released.
/// </summary>
internal sealed class FailResultHandler(
    IBranchProvider branchProvider,
    IRunProvider runProvider,
    IRunCountersProvider runCountersProvider,
    IRunDispatcher runDispatcher,
    IIdGenerator idGenerator,
    TimeProvider clock,
    IRunCancellationService runCancellationService,
    ICompensationOrchestrator compensationOrchestrator,
    IJoinAggregatorProvider joinAggregatorProvider,
    IRunJournal journal,
    IBranchTerminator branchTerminator,
    ILogger<FailResultHandler>? logger = null) : BranchResultHandler<NodeExecutionResult.Fail>
{
    /// <summary>
    /// Local-state key marking a branch as a member of a fan-out cohort (written by ForkNodeExecutor
    /// and ParallelForEachNodeExecutor, read by JoinNodeExecutor). Kept here too because the fail
    /// path must contribute on behalf of a branch that will never reach the Join node.
    /// </summary>
    internal const string JoinTokenKey = "__join_token";

    private readonly OnFailureHandler _onFailureHandler = new();
    private readonly ContinueResultHandler _continueHandler = new(branchProvider, journal, branchTerminator);

    public override async Task<BranchResultOutcome> HandleAsync(NodeExecutionResult.Fail result, BranchResultContext context, CancellationToken ct)
    {
        NodeExecutionResult handledResult = _onFailureHandler.Apply(result, context.Node, context.NodeContext);
        if (handledResult is NodeExecutionResult.Continue handledContinue)
        {
            return await _continueHandler.HandleAsync(handledContinue, context, ct);
        }

        RunRow runRow = context.Run;
        BranchRow branchRow = context.Branch;
        long branchId = context.BranchId;
        Guid nodeId = context.Node.NodeId;

        string errorJson = JsonSerializer.Serialize(new { result.ErrorCode, result.Message }, EngineJson.Options);
        OnFailureConfig? config = (context.Node as BaseActionNode)?.OnFailure;
        ErrorOutcome outcome = config?.Outcome ?? ErrorOutcome.FailBranch;
        bool shouldCompensate = (outcome == ErrorOutcome.Compensate) || (context.Definition.RunCompensationOnFailure);

        if (outcome == ErrorOutcome.FailRun)
        {
            await branchProvider.SetFailedAsync(branchId, errorJson, ct);
            await runProvider.TransitionStatusAsync(runRow.Id, RunStatus.Running, RunStatus.Failing, null, null, ct);
            await runCancellationService.RequestCancellationAsync(runRow.Id, $"FailRun cascade triggered by node {nodeId}", ct);
        }
        else if (shouldCompensate)
        {
            branchRow = await branchProvider.UpdatePointerAsync(branchId, nodeId, BranchStatus.Compensating, branchRow.LocalJson, branchRow.LastOutputJson, ct);
            await compensationOrchestrator.RunAsync(runRow.Id, branchId, ct);
            await branchProvider.SetFailedAsync(branchId, errorJson, ct);
        }
        else
        {
            await branchProvider.SetFailedAsync(branchId, errorJson, ct);
            if (context.Definition.FailFast)
            {
                await runProvider.TransitionStatusAsync(runRow.Id, RunStatus.Running, RunStatus.Failing, null, null, ct);
            }
        }

        if (string.Equals(result.ErrorCode, "OUT_OF_CREDITS", StringComparison.Ordinal))
        {
            // Credit exhaustion is a run-wide constraint - cascade cancellation so sibling
            // branches stop instead of continuing to burn (already-exhausted) budget.
            await runCancellationService.RequestCancellationAsync(runRow.Id, "OUT_OF_CREDITS", ct);
        }

        // The branch is over. Only NodeFailed was recorded before, so a reader could see
        // which node failed but not that the branch itself ended, nor how it was handled -
        // FailBranch, FailRun and Compensate look identical from NodeFailed alone.
        await journal.AppendBranchEventAsync(
            runRow.Id,
            branchRow.RefId,
            nodeId,
            HistoryEventKind.BranchFailed,
            new { result.ErrorCode, result.Message, onFailure = outcome.ToString(), compensated = shouldCompensate },
            ct);

        // This branch is dying, so it will never reach its Join. Contribute on its behalf,
        // otherwise a Mode=All cohort never reaches ExpectedCount and everything after the
        // Join is silently skipped until the reaper eventually cancels the run.
        await ContributeFailureToJoinAsync(runRow, branchRow, context.Definition, ct);

        await branchTerminator.ReleaseAsync(runRow.Id, 1, ct);
        return BranchResultOutcome.Stopped;
    }

    /// <summary>
    /// Records a dying branch's "failed" arrival at the Join its cohort converges on, and — if that
    /// arrival is the one that satisfies the quorum — spawns the continuation branch the Join would
    /// otherwise have continued on.
    ///
    /// A branch that fails never reaches the Join node, so without this a Mode=All cohort can never
    /// reach ExpectedCount: the aggregator stays unclaimed, everything downstream of the Join is
    /// silently skipped, and the run sits Running until the reaper collects it.
    ///
    /// The continuation is a fresh branch rather than a resurrection of the failed one: the failed
    /// branch keeps its Failed status (so the run still finalizes as Failed/PartiallyFailed), while
    /// the work after the Join proceeds. Best-effort — a failure here must not mask the original node
    /// failure that brought us into this path.
    /// </summary>
    private async Task ContributeFailureToJoinAsync(RunRow runRow, BranchRow branchRow, WorkflowDefinition definition, CancellationToken ct)
    {
        IReadOnlyDictionary<string, JsonElement> localState = BranchLocalState.Deserialize(branchRow.LocalJson);
        if (!localState.TryGetValue(JoinTokenKey, out JsonElement tokenElement)
            || tokenElement.ValueKind != JsonValueKind.String
            || !Guid.TryParse(tokenElement.GetString(), out Guid joinToken))
        {
            // Not part of a Fork/ParallelForEach cohort - nothing to contribute to.
            return;
        }

        try
        {
            JoinContributionResult contribution = await joinAggregatorProvider.ContributeAsync(joinToken, branchRow.Id, "failed", ct);
            logger?.LogInformation(
                "Branch {BranchId} failed and contributed 'failed' to join {JoinToken} ({Contributed}/{Expected}, {Failed} failed).",
                branchRow.Id, joinToken, contribution.ContributedCount, contribution.ExpectedCount, contribution.FailedCount);

            if (!contribution.ShouldContinue || contribution.JoinNodeId is not Guid joinNodeId)
            {
                return;
            }

            Guid? continuationNodeId = WorkflowGraph.ResolveTarget(definition, joinNodeId, "default");
            if (continuationNodeId is null)
            {
                // The Join's "default" port leads nowhere, so there is nothing after it to run.
                return;
            }

            string continuationLocalJson = BranchLocalState.Serialize(new Dictionary<string, JsonElement>
            {
                ["joinContributed"] = JsonSerializer.SerializeToElement(contribution.ContributedCount),
                ["joinSucceeded"] = JsonSerializer.SerializeToElement(contribution.SucceededCount),
                ["joinFailed"] = JsonSerializer.SerializeToElement(contribution.FailedCount)
            });

            // Count the continuation BEFORE creating it, so the caller's own decrement cannot drive
            // the run to zero active branches and finalize it out from under the new branch.
            await runCountersProvider.IncrementActiveBranchesAsync(runRow.Id, 1, ct);

            BranchRow continuation = await branchProvider.CreateAsync(new BranchRow
            {
                Id = 0,
                RefId = idGenerator.NewId(),
                RunId = runRow.Id,
                ParentBranchId = branchRow.RefId,
                ForkCohortId = branchRow.ForkCohortId,
                NodeId = continuationNodeId.Value,
                Status = BranchStatus.Active,
                PendingTakePort = null,
                LocalJson = continuationLocalJson,
                LastOutputJson = null,
                CompensationStackJson = branchRow.CompensationStackJson,
                CreatedAt = clock.GetUtcNow().UtcDateTime,
                UpdatedAt = clock.GetUtcNow().UtcDateTime,
                RowVersion = Array.Empty<byte>()
            }, ct);

            await journal.AppendBranchEventAsync(runRow.Id, continuation.RefId, joinNodeId, HistoryEventKind.JoinContinuationSpawned, ct);
            await runDispatcher.DispatchAsync(new BranchExecutionRequest(runRow.Id, continuation.Id, BranchExecutionReason.ForkChild), ct);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to contribute branch {BranchId}'s failure to its join cohort on run {RunId}.", branchRow.Id, runRow.Id);
        }
    }
}

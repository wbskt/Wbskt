using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime.BranchResults;

/// <summary>
/// <see cref="NodeExecutionResult.Fork"/>: fans the branch out. A single child with no continue port is
/// taken inline by this branch. Otherwise every child is a new branch in one fork cohort, counted
/// before it is created and dispatched once it exists, and this branch then either moves on through
/// the continue port or completes.
/// </summary>
internal sealed class ForkResultHandler(
    IBranchProvider branchProvider,
    IRunCountersProvider runCountersProvider,
    IRunDispatcher runDispatcher,
    IIdGenerator idGenerator,
    TimeProvider clock,
    IRunJournal journal,
    IBranchTerminator branchTerminator) : BranchResultHandler<NodeExecutionResult.Fork>
{
    private readonly BranchEndings _endings = new(branchProvider, journal, branchTerminator);

    public override async Task<BranchResultOutcome> HandleAsync(NodeExecutionResult.Fork result, BranchResultContext context, CancellationToken ct)
    {
        BranchRow branchRow = context.Branch;
        Guid nodeId = context.Node.NodeId;
        IReadOnlyDictionary<string, JsonElement> baseLocalState = BranchLocalState.Merge(branchRow.LocalJson, result.LocalStatePatch);
        if (result.Children.Count == 1 && string.IsNullOrWhiteSpace(result.ContinueOutboundPort))
        {
            ForkSpec child = result.Children.Single();
            string childLocalJson = BranchLocalState.Serialize(BranchLocalState.Merge(BranchLocalState.Serialize(baseLocalState), child.LocalState));
            BranchRow moved = await branchProvider.UpdatePointerAsync(context.BranchId, BranchRouting.ResolveForkTarget(context.Definition, nodeId, child.OutboundPort), BranchStatus.Active, childLocalJson, branchRow.LastOutputJson, ct);
            return BranchResultOutcome.Advanced(moved);
        }

        if (result.Children.Count > 0)
        {
            await runCountersProvider.IncrementActiveBranchesAsync(context.Run.Id, result.Children.Count, ct);
            Guid cohortId = idGenerator.NewId();
            foreach (ForkSpec child in result.Children)
            {
                string childLocalJson = BranchLocalState.Serialize(BranchLocalState.Merge(BranchLocalState.Serialize(baseLocalState), child.LocalState));
                BranchRow created = await branchProvider.CreateAsync(new BranchRow
                {
                    Id = 0,
                    RefId = idGenerator.NewId(),
                    RunId = context.Run.Id,
                    ParentBranchId = branchRow.RefId,
                    ForkCohortId = cohortId,
                    NodeId = BranchRouting.ResolveForkTarget(context.Definition, nodeId, child.OutboundPort),
                    Status = BranchStatus.Active,
                    PendingTakePort = null,
                    LocalJson = childLocalJson,
                    LastOutputJson = null,
                    CompensationStackJson = branchRow.CompensationStackJson,
                    CreatedAt = clock.GetUtcNow().UtcDateTime,
                    UpdatedAt = clock.GetUtcNow().UtcDateTime,
                    RowVersion = Array.Empty<byte>()
                }, ct);
                await runDispatcher.DispatchAsync(new BranchExecutionRequest(context.RunId, created.Id, BranchExecutionReason.ForkChild), ct);
            }
        }

        if (string.IsNullOrWhiteSpace(result.ContinueOutboundPort))
        {
            await _endings.CompleteAsync(context.Run.Id, context.BranchId, branchRow.RefId, ct);
            return BranchResultOutcome.Stopped;
        }

        string continueLocalJson = BranchLocalState.Serialize(baseLocalState);
        BranchRow continued = await branchProvider.UpdatePointerAsync(context.BranchId, BranchRouting.ResolveForkTarget(context.Definition, nodeId, result.ContinueOutboundPort), BranchStatus.Active, continueLocalJson, branchRow.LastOutputJson, ct);
        return BranchResultOutcome.Advanced(continued);
    }
}

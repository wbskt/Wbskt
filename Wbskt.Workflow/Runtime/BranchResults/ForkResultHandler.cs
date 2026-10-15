using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.NodeExecutors.Controls;

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
    BranchEndings endings,
    ILogger<ForkResultHandler>? logger = null) : BranchResultHandler<NodeExecutionResult.Fork>
{
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
            await CreateChildrenAsync(result, context, baseLocalState, ct);
        }

        if (string.IsNullOrWhiteSpace(result.ContinueOutboundPort))
        {
            await endings.CompleteAsync(context.Run.Id, context.BranchId, branchRow.RefId, ct);
            return BranchResultOutcome.Stopped;
        }

        string continueLocalJson = BranchLocalState.Serialize(baseLocalState);
        BranchRow continued = await branchProvider.UpdatePointerAsync(context.BranchId, BranchRouting.ResolveForkTarget(context.Definition, nodeId, result.ContinueOutboundPort), BranchStatus.Active, continueLocalJson, branchRow.LastOutputJson, ct);
        return BranchResultOutcome.Advanced(continued);
    }

    /// <summary>
    /// Counts, creates and dispatches the cohort's children, so that a Fork replayed for the same visit
    /// creates no second cohort.
    /// </summary>
    /// <remarks>
    /// The children are created and dispatched before this branch's pointer moves past the Fork, and the
    /// provider writes each row on its own connection, so the two cannot share a transaction. A crash in
    /// between left the branch on the Fork; recovery re-ran it and created a second set of children in a
    /// new cohort, all running the same work. The cohort and child ids are therefore derived from the
    /// branch's visit (its RefId, the Fork node and the row version the Fork ran at, which only a write to
    /// the branch changes), and each child is created only if absent: a replay finds the children it
    /// already made, leaves them be (recovery or the first pass dispatched them), and creates and
    /// dispatches only the ones the crash cut off. A branch row with no row version (a fake that keeps
    /// none) cannot name its visit, so its cohort is a fresh id as before.
    /// </remarks>
    private async Task CreateChildrenAsync(
        NodeExecutionResult.Fork result,
        BranchResultContext context,
        IReadOnlyDictionary<string, JsonElement> baseLocalState,
        CancellationToken ct)
    {
        BranchRow branchRow = context.Branch;
        Guid nodeId = context.Node.NodeId;
        bool visitKnown = branchRow.RowVersion.Length > 0;
        Guid cohortId = visitKnown
            ? DeriveId($"fork-cohort:{branchRow.RefId:N}:{nodeId:N}:{Convert.ToHexString(branchRow.RowVersion)}")
            : idGenerator.NewId();

        // Counted before any child exists, as always, so a child's own end can never drive the run to
        // zero active branches while its siblings are still being created. A replay counts again and
        // hands back below what its children did not need; a crash before that leaves the run
        // over-counted, which the reaper collects, rather than under-counted, which finalises a run
        // that is still running.
        await runCountersProvider.IncrementActiveBranchesAsync(context.Run.Id, result.Children.Count, ct);

        List<ForkSpec> children = result.Children.ToList();
        int alreadyCreated = 0;
        Dictionary<string, JsonElement>? adoptedJoinState = null;
        for (int index = 0; index < children.Count; index++)
        {
            ForkSpec child = children[index];
            IReadOnlyDictionary<string, JsonElement> childState = BranchLocalState.Merge(BranchLocalState.Serialize(baseLocalState), child.LocalState);
            if (adoptedJoinState is not null)
            {
                // The replayed Fork opened a new join cohort, but the children made on the first pass
                // carry the first one's token; a child created now must join theirs, or neither cohort
                // ever reaches its expected count.
                childState = BranchLocalState.Merge(BranchLocalState.Serialize(childState), adoptedJoinState);
            }

            (BranchRow created, bool isNew) = await branchProvider.CreateIfAbsentAsync(new BranchRow
            {
                Id = 0,
                RefId = visitKnown ? DeriveId($"fork-child:{cohortId:N}:{index}") : idGenerator.NewId(),
                RunId = context.Run.Id,
                ParentBranchId = branchRow.RefId,
                ForkCohortId = cohortId,
                NodeId = BranchRouting.ResolveForkTarget(context.Definition, nodeId, child.OutboundPort),
                Status = BranchStatus.Active,
                PendingTakePort = null,
                LocalJson = BranchLocalState.Serialize(childState),
                LastOutputJson = null,
                CompensationStackJson = branchRow.CompensationStackJson,
                CreatedAt = clock.GetUtcNow().UtcDateTime,
                UpdatedAt = clock.GetUtcNow().UtcDateTime,
                RowVersion = Array.Empty<byte>()
            }, ct);

            if (!isNew)
            {
                alreadyCreated++;
                adoptedJoinState = JoinStateOf(created.LocalJson);
                continue;
            }

            await runDispatcher.DispatchAsync(new BranchExecutionRequest(context.RunId, created.Id, BranchExecutionReason.ForkChild), ct);
        }

        if (alreadyCreated > 0)
        {
            logger?.LogWarning(
                "Fork node {NodeId} on run {RunId} branch {BranchId} was replayed; {Existing} of its {Count} children already existed and were not created again.",
                nodeId, context.Run.Id, context.BranchId, alreadyCreated, result.Children.Count);

            // This branch is still live and counted, so handing back the surplus cannot reach zero.
            await runCountersProvider.DecrementActiveBranchesAsync(context.Run.Id, alreadyCreated, ct);
        }
    }

    private static Dictionary<string, JsonElement>? JoinStateOf(string localJson)
    {
        IReadOnlyDictionary<string, JsonElement> state = BranchLocalState.Deserialize(localJson);
        var joinState = new Dictionary<string, JsonElement>();
        foreach (string key in new[] { JoinTokens.TokenKey, JoinTokens.OuterKey })
        {
            if (state.TryGetValue(key, out JsonElement value))
            {
                joinState[key] = value;
            }
        }

        return joinState.Count > 0 ? joinState : null;
    }

    private static Guid DeriveId(string name)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(name));
        byte[] bytes = digest[..16];

        // Stamp it as a name-based (version 5 layout, RFC 4122 variant) GUID so it reads as one.
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}

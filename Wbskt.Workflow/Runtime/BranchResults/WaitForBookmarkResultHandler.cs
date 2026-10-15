using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Channels;

namespace Wbskt.Workflow.Runtime.BranchResults;

/// <summary>
/// <see cref="NodeExecutionResult.WaitForBookmark"/>: parks the branch. It is marked Waiting at the
/// node, the park is recorded, the one bookmark row its wake condition describes is written, and only
/// then does the node's <see cref="NodeExecutionResult.WaitForBookmark.AfterParked"/> follow-up run.
/// This is the one place a bookmark row is built from a <see cref="WakeCondition"/>.
/// </summary>
internal sealed class WaitForBookmarkResultHandler(
    IBranchProvider branchProvider,
    IBookmarkProvider bookmarkProvider,
    IIdGenerator idGenerator,
    TimeProvider clock,
    IRunJournal journal,
    ILogger<WaitForBookmarkResultHandler>? logger = null) : BranchResultHandler<NodeExecutionResult.WaitForBookmark>
{
    public override async Task<BranchResultOutcome> HandleAsync(NodeExecutionResult.WaitForBookmark result, BranchResultContext context, CancellationToken ct)
    {
        string localJson = BranchLocalState.Serialize(BranchLocalState.Merge(context.Branch.LocalJson, result.LocalStatePatch));
        BranchRow branchRow = await branchProvider.UpdatePointerAsync(context.BranchId, context.Branch.NodeId, BranchStatus.Waiting, localJson, context.Branch.LastOutputJson, ct);
        await journal.AppendBranchEventAsync(context.Run.Id, branchRow.RefId, branchRow.NodeId, HistoryEventKind.BranchParked, ct);

        // Single-row model (design §3.5): one bookmark row per wait. A TTL, if present,
        // lives on the same row (ExpiresAt/TtlPort) rather than a separate companion timer
        // row - whichever side (signal/http/child match vs. TTL expiry) claims the row
        // first wins; the loser's claim is a no-op.
        DateTime nowUtc = clock.GetUtcNow().UtcDateTime;
        await bookmarkProvider.CreateAsync(CreateBookmarkRow(context.Run.Id, branchRow.RefId, branchRow.NodeId, result.Condition, nowUtc), ct);

        if (result.AfterParked is { } afterParked)
        {
            try
            {
                await afterParked(ct);
            }
            catch (Exception ex)
            {
                // The branch is parked, so a failure here is logged rather than failing a
                // branch that is already waiting. The follow-up wakes its own wait when it
                // can (a command that could not be sent leaves by "failed"); otherwise the
                // bookmark's TTL moves it on.
                logger?.LogError(ex, "The follow-up of node {NodeId} failed after branch {BranchId} of run {RunId} parked.", branchRow.NodeId, context.BranchId, context.RunId);
            }
        }

        return BranchResultOutcome.Stopped;
    }

    private BookmarkRow CreateBookmarkRow(int runId, Guid branchRefId, Guid nodeId, WakeCondition condition, DateTime nowUtc)
    {
        return new BookmarkRow
        {
            Id = 0,
            RefId = idGenerator.NewId(),
            RunId = runId,
            BranchRefId = branchRefId,
            NodeId = nodeId,
            WakeConditionKind = condition.Kind,
            MatchKey = GetWakeConditionMatchKey(condition),
            WakeConditionJson = JsonSerializer.Serialize(condition, EngineJson.Options),
            ExpiresAt = condition.ExpiresAt(nowUtc),
            TtlPort = condition.TtlPort,
            CreatedAt = nowUtc
        };
    }

    internal static string GetWakeConditionMatchKey(WakeCondition condition)
    {
        // Built by the same channel that computes an event's correlation key, so an inbound event on
        // that channel finds this bookmark. The key formats belong to the channels, which the models
        // project cannot see, so the mapping lives here; an unmapped condition throws rather than
        // parking on an empty key that no event would ever match.
        return condition switch
        {
            // Timers aren't matched by an inbound event key — they're woken by ExpiresAt alone.
            TimerWakeCondition => string.Empty,
            SignalWakeCondition signal => SignalChannel.Key(signal.Name, signal.Correlation),
            HttpWakeCondition http => HttpWakeChannel.Key(http.Token),
            ChildRunCompletedWakeCondition child => ChildCompletedChannel.Key(child.ChildRunId.ToString()),
            CommandOutcomeWakeCondition command => CommandChannel.Key(command.CommandId),
            _ => throw new InvalidOperationException($"Wake condition {condition.GetType().Name} has no match key.")
        };
    }
}

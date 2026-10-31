using System.Globalization;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

/// <summary>
/// The one way a node parks its branch and picks it up again: Delay, AwaitSignal, WaitForHttp,
/// SubWorkflow and a Command step that waits for its answer all run through here.
///
/// <para>A park writes a <see cref="ParkMarker"/> naming the node under <see cref="ParkedKey"/>, and the
/// absolute deadline under <see cref="DeadlineKey"/> when the node keeps one. The engine runs the same
/// node again when its bookmark fires, and this node's own marker is what tells that visit from a first
/// one. Every exit from a resume removes the same set of keys (<see cref="ParkStateKeys"/>): the marker,
/// the deadline and the wake. The branch loop removes that same set when a TTL sends the branch out by a
/// take-port without running the node, so no exit leaves a marker behind for the node's next loop lap.</para>
///
/// <para>Each executor used to read its own marker and clear its own keys, and they differed on a wake
/// that was not theirs: one continued on it, one parked afresh and sent its command again, one kept
/// waiting. The rule now lives here. A <c>__wake</c> on the branch is this node's only when the resumer
/// tagged it for this node (or it predates the tag) and the executor recognises it as the answer to what
/// it parked on; any other wake is stale. A stale wake is cleared and never consumed: the node keeps
/// waiting on what it parked on for whatever is left of its deadline, or takes its deadline exit when
/// none is left. A first visit clears a stale wake as it parks, so the next real wake is not mixed up
/// with it.</para>
/// </summary>
internal abstract class ParkingNodeExecutor : INodeExecutor
{
    protected ParkingNodeExecutor(TimeProvider clock)
    {
        Clock = clock;
    }

    protected TimeProvider Clock { get; }

    public abstract string Kind { get; }

    public virtual bool IsReplaySafe(BaseNode node) => false;

    /// <summary>Local-state key holding the marker of the node that has parked.</summary>
    protected abstract string ParkedKey { get; }

    /// <summary>
    /// Local-state key holding the round-trippable deadline, or null for a node that keeps none (its
    /// bookmark may still carry a TTL; the node then has nothing to measure what is left of it by).
    /// </summary>
    protected virtual string? DeadlineKey => null;

    /// <summary>
    /// Every key a park leaves on the branch, which is what every exit from it removes: by the node on
    /// its resume visit, and by the branch loop when a TTL takes the node's timeout port without running it.
    /// </summary>
    internal IReadOnlyCollection<string> ParkStateKeys => DeadlineKey is { } deadlineKey
        ? [ParkedKey, deadlineKey, .. ParkMarker.WakeKeys]
        : [ParkedKey, .. ParkMarker.WakeKeys];

    /// <summary>What a node parks on, what it records as waited on, and when it gives up (null: never).</summary>
    protected readonly record struct ParkPlan(WakeCondition Condition, string WaitingOn, DateTime? Deadline);

    /// <summary>What a resume visit does: leave by a port, go on waiting, or start over as a first visit.</summary>
    protected abstract record ResumeStep
    {
        private ResumeStep()
        {
        }

        /// <summary>Go on waiting on what the node parked on (see <see cref="KeepWaitingOnAsync"/>).</summary>
        public static ResumeStep KeepWaiting { get; } = new Wait();

        /// <summary>The park is spent: run the first visit again, which parks afresh.</summary>
        public static ResumeStep StartOver { get; } = new Restart();

        /// <summary>Leave by <paramref name="Port"/>, writing <paramref name="Patch"/> on the way out.</summary>
        public sealed record Leave(string Port, IReadOnlyDictionary<string, JsonElement>? Patch = null) : ResumeStep;

        internal sealed record Wait : ResumeStep;

        internal sealed record Restart : ResumeStep;
    }

    /// <summary>The visit that finds no park of this node on the branch. It parks with <see cref="Park"/>, or does not.</summary>
    protected abstract Task<NodeExecutionResult> FirstVisitAsync(NodeContext ctx, CancellationToken ct);

    /// <summary>
    /// The exit for a wake tagged for this node, or null when the wake is not the answer to what the
    /// node parked on (<paramref name="waitingOn"/>); a null makes the wake stale.
    /// </summary>
    protected abstract ResumeStep.Leave? OnWake(NodeContext ctx, string? waitingOn, JsonElement wake);

    /// <summary>
    /// A resume visit with no wake of this node's on the branch: none at all, or a stale one once going
    /// on waiting is not possible (the deadline has passed, or <see cref="KeepWaitingOnAsync"/> has nothing).
    /// <paramref name="deadlinePassed"/> is false when the node keeps no deadline.
    /// </summary>
    protected abstract ResumeStep OnNoWake(NodeContext ctx, string? waitingOn, bool deadlinePassed);

    /// <summary>
    /// The condition to go on waiting on: the one the node parked on. The base cuts its TTL to what is
    /// left of the deadline. Null when the marker does not say what that was; a stale wake then goes to
    /// <see cref="OnNoWake"/>, and a <see cref="ResumeStep.KeepWaiting"/> starts over.
    /// </summary>
    protected abstract ValueTask<WakeCondition?> KeepWaitingOnAsync(NodeContext ctx, string? waitingOn, DateTime? deadline, CancellationToken ct);

    /// <summary>
    /// True when the branch holds a park of this node written before the marker named its node and
    /// that the marker check does not recognise. Only a Delay has such a shape.
    /// </summary>
    protected virtual bool HasUnmarkedPark(IReadOnlyDictionary<string, JsonElement> state) => false;

    public async Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        IReadOnlyDictionary<string, JsonElement> state = ctx.Branch.LocalState;
        Guid nodeId = ctx.Node.NodeId;

        bool resuming = ParkMarker.TryReadOwn(state, ParkedKey, nodeId, out string? waitingOn) || HasUnmarkedPark(state);
        if (!resuming)
        {
            return await FirstVisitAsync(ctx, ct);
        }

        DateTime? deadline = TryReadDeadline(state, out DateTime value) ? value : null;
        bool deadlinePassed = deadline is DateTime due && Clock.GetUtcNow().UtcDateTime >= due;

        ParkMarker.WakeOwner owner = ParkMarker.ReadWake(state, nodeId, out JsonElement wake);
        if (owner == ParkMarker.WakeOwner.This && OnWake(ctx, waitingOn, wake) is { } woken)
        {
            return Leave(woken);
        }

        // Any wake still on the branch here is stale. It is not consumed: while time is left the node
        // goes on waiting on the same thing, and once none is left it takes its deadline exit.
        ResumeStep step = owner != ParkMarker.WakeOwner.None && !deadlinePassed
            ? ResumeStep.KeepWaiting
            : OnNoWake(ctx, waitingOn, deadlinePassed);

        switch (step)
        {
            case ResumeStep.Leave exit:
                return Leave(exit);

            case ResumeStep.Wait:
                if (await KeepWaitingOnAsync(ctx, waitingOn, deadline, ct) is { } condition)
                {
                    return KeepWaiting(condition, deadline);
                }

                // Nothing to go on waiting on: decide as if no wake had come.
                if (owner != ParkMarker.WakeOwner.None && OnNoWake(ctx, waitingOn, deadlinePassed) is ResumeStep.Leave staleExit)
                {
                    return Leave(staleExit);
                }

                break;
        }

        // The park is spent: start over. A new park overwrites the marker and clears a stale wake.
        return await FirstVisitAsync(ctx, ct);
    }

    private NodeExecutionResult.WaitForBookmark KeepWaiting(WakeCondition condition, DateTime? deadline)
    {
        if (condition.Ttl is not null && deadline is DateTime until)
        {
            TimeSpan remaining = until - Clock.GetUtcNow().UtcDateTime;
            condition = condition with { Ttl = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero };
        }

        // The marker and deadline already on the branch stay as they are; a stale wake goes.
        return new NodeExecutionResult.WaitForBookmark(condition, new Dictionary<string, JsonElement>())
        {
            RemoveKeys = ParkMarker.WakeKeys
        };
    }

    /// <summary>The first-visit park: the marker, the deadline when there is one, and a stale wake cleared.</summary>
    protected NodeExecutionResult.WaitForBookmark Park(NodeContext ctx, ParkPlan plan)
    {
        var patch = new Dictionary<string, JsonElement>
        {
            [ParkedKey] = ParkMarker.Create(ctx.Node.NodeId, plan.WaitingOn)
        };
        if (DeadlineKey is { } deadlineKey && plan.Deadline is DateTime deadline)
        {
            patch[deadlineKey] = JsonSerializer.SerializeToElement(deadline.ToString("O"));
        }

        // Any wake on the branch now was delivered before this park existed, so it is not this park's
        // answer; left there, it would sit beside the real one until the node's exit.
        return new NodeExecutionResult.WaitForBookmark(plan.Condition, patch)
        {
            RemoveKeys = ParkMarker.WakeKeys
        };
    }

    private NodeExecutionResult.Continue Leave(ResumeStep.Leave exit)
    {
        return new NodeExecutionResult.Continue(exit.Port, exit.Patch ?? new Dictionary<string, JsonElement>(), ParkStateKeys);
    }

    private bool TryReadDeadline(IReadOnlyDictionary<string, JsonElement> state, out DateTime deadline)
    {
        deadline = default;
        return DeadlineKey is { } key
            && state.TryGetValue(key, out JsonElement element)
            && element.ValueKind == JsonValueKind.String
            && DateTime.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out deadline);
    }
}

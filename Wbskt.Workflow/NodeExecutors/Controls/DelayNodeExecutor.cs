using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

/// <summary>
/// Durable delay. On first visit it parks the branch with a <see cref="TimerWakeCondition"/>
/// at now + duration (the BookmarkScheduler resumes the branch when the timer is due). On the
/// resume visit it continues. First-visit vs resume is told apart by the park marker, as for every
/// parking node (see <see cref="ParkingNodeExecutor"/>), because the engine re-executes the same node
/// when a bookmark fires.
/// </summary>
internal sealed class DelayNodeExecutor(TimeProvider clock) : ParkingNodeExecutor(clock)
{
    internal const string DelayUntilKey = BranchStateKeys.DelayUntil;

    private const string DefaultPort = "default";

    public override string Kind => NodeKind.ControlDelay;

    // Only computes a wake time from branch state; the bookmark is registered by the engine from the
    // result, so a replay simply parks again (on a first visit, from a slightly later instant).
    public override bool IsReplaySafe(BaseNode node) => true;

    protected override string ParkedKey => BranchStateKeys.Delay;

    protected override string DeadlineKey => DelayUntilKey;

    protected override Task<NodeExecutionResult> FirstVisitAsync(NodeContext ctx, CancellationToken ct)
    {
        var node = (DelayNode)ctx.Node;
        TimeSpan duration = node.Config?.Duration ?? TimeSpan.Zero;
        if (duration <= TimeSpan.Zero)
        {
            return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Continue(DefaultPort, new Dictionary<string, JsonElement>()));
        }

        DateTime wakeAt = Clock.GetUtcNow().UtcDateTime + duration;
        return Task.FromResult<NodeExecutionResult>(Park(ctx, new ParkPlan(new TimerWakeCondition(wakeAt), wakeAt.ToString("O"), wakeAt)));
    }

    // A timer carries no payload, so no wake is ever the answer to a Delay; one on the branch is stale.
    protected override ResumeStep.Leave? OnWake(NodeContext ctx, string? waitingOn, JsonElement wake) => null;

    // The timer fired: continue. Re-dispatched before it was actually due: re-park on the same instant.
    // The exit clears the deadline. Without that a Delay revisited in a loop sees a deadline already in
    // the past and continues immediately - so it waits on the first lap only and the loop then spins.
    protected override ResumeStep OnNoWake(NodeContext ctx, string? waitingOn, bool deadlinePassed)
    {
        return deadlinePassed ? new ResumeStep.Leave(DefaultPort) : ResumeStep.KeepWaiting;
    }

    protected override WakeCondition? KeepWaitingOn(NodeContext ctx, string? waitingOn, DateTime? deadline)
    {
        return deadline is DateTime wakeAt ? new TimerWakeCondition(wakeAt) : null;
    }

    // A Delay parked before it wrote a marker left only its deadline. Every exit clears the deadline,
    // so a deadline with no marker beside it is still that park.
    protected override bool HasUnmarkedPark(IReadOnlyDictionary<string, JsonElement> state)
    {
        return !state.ContainsKey(ParkedKey) && state.ContainsKey(DelayUntilKey);
    }
}

using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

/// <summary>
/// Control nodes that park a branch until an inbound event arrives on some correlation channel, or
/// until a TTL elapses. The park, the resume and the marker cleanup are <see cref="ParkingNodeExecutor"/>'s;
/// what these share on top is the exits. A resume leaves via the "default" port (promoting the inbound
/// payload) when the resumer delivered a wake body, and via the "timeout" port once the deadline has
/// passed. Derived executors differ only in which wake condition they park on and the local-state key
/// names they use - see <see cref="AwaitSignalNodeExecutor"/> and <see cref="WaitForHttpNodeExecutor"/>.
/// </summary>
internal abstract class AwaitInboundNodeExecutor(TimeProvider clock) : ParkingNodeExecutor(clock)
{
    private const string DefaultPort = "default";
    protected const string DefaultTimeoutPort = "timeout"; // edges map the port to the target node

    // Reads branch state and returns a park or a continue; the bookmark itself is registered by the
    // engine from the result, so a replay produces the same park and writes nothing of its own.
    public override bool IsReplaySafe(BaseNode node) => true;

    /// <summary>Local-state key holding the round-trippable deadline timestamp (absent when no TTL).</summary>
    protected abstract override string DeadlineKey { get; }

    /// <summary>Key the inbound payload is promoted under when the branch resumes via a wake.</summary>
    protected abstract string WakePayloadKey { get; }

    /// <summary>
    /// Builds the first-visit park plan: the wake condition to register, what the node waits on
    /// (recorded in the marker), and the absolute deadline (null parks with no TTL).
    /// </summary>
    protected abstract ParkPlan CreateParkPlan(NodeContext ctx);

    protected override Task<NodeExecutionResult> FirstVisitAsync(NodeContext ctx, CancellationToken ct)
    {
        return Task.FromResult<NodeExecutionResult>(Park(ctx, CreateParkPlan(ctx)));
    }

    // The resumer stores the inbound payload under "__wake" when an event arrives; its presence
    // distinguishes an event wake (continue via "default", promoting the payload) from a TTL timeout
    // (a timer wake carries no payload).
    protected override ResumeStep.Leave? OnWake(NodeContext ctx, string? waitingOn, JsonElement wake)
    {
        return new ResumeStep.Leave(DefaultPort, PromoteWake(wake, WakePayloadKey));
    }

    protected override ResumeStep OnNoWake(NodeContext ctx, string? waitingOn, bool deadlinePassed)
    {
        return new ResumeStep.Leave(deadlinePassed ? DefaultTimeoutPort : DefaultPort);
    }

    protected override WakeCondition? KeepWaitingOn(NodeContext ctx, string? waitingOn, DateTime? deadline)
    {
        return CreateParkPlan(ctx).Condition;
    }

    // Surface the caller-supplied "body" from the raw wake payload under a friendly key.
    private static IReadOnlyDictionary<string, JsonElement> PromoteWake(JsonElement wake, string key)
    {
        JsonElement payload = wake.ValueKind == JsonValueKind.Object && wake.TryGetProperty("body", out JsonElement body)
            ? body
            : wake;
        return new Dictionary<string, JsonElement> { [key] = payload.Clone() };
    }
}

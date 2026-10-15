using System.Globalization;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

/// <summary>
/// Shared machine for control nodes that park a branch until an inbound event arrives on some
/// correlation channel, or until a TTL elapses. First visit registers a bookmark on the wake
/// condition supplied by the derived executor and writes a local-state marker; the engine
/// re-executes the same node on resume, where the presence of this node's marker (see
/// <see cref="ParkMarker"/>) distinguishes a resume from a first visit. A resume leaves via the
/// "default" port (promoting any inbound payload) when the resumer delivered a wake body, and via the
/// "timeout" port once the deadline has passed; either way it clears its marker, deadline and wake.
/// Derived executors differ only in which wake condition they park on and the local-state key names
/// they use - see <see cref="AwaitSignalNodeExecutor"/> and <see cref="WaitForHttpNodeExecutor"/>.
/// </summary>
internal abstract class AwaitInboundNodeExecutor : INodeExecutor
{
    private const string DefaultPort = "default";
    protected const string DefaultTimeoutPort = "timeout"; // edges map the port to the target node

    protected AwaitInboundNodeExecutor(TimeProvider clock)
    {
        Clock = clock;
    }

    protected TimeProvider Clock { get; }

    public abstract string Kind { get; }

    // Reads branch state and returns a park or a continue; the bookmark itself is registered by the
    // engine from the result, so a replay produces the same park and writes nothing of its own.
    public bool IsReplaySafe(BaseNode node) => true;

    /// <summary>Local-state key holding the marker of the node of this kind that has parked.</summary>
    protected abstract string ParkedKey { get; }

    /// <summary>Local-state key holding the round-trippable deadline timestamp (absent when no TTL).</summary>
    protected abstract string DeadlineKey { get; }

    /// <summary>Key the inbound payload is promoted under when the branch resumes via a wake.</summary>
    protected abstract string WakePayloadKey { get; }

    /// <summary>
    /// Builds the first-visit park plan: the wake condition to register, what the node waits on
    /// (recorded in the marker under <see cref="ParkedKey"/>), and the absolute deadline (null parks
    /// with no TTL).
    /// </summary>
    protected abstract ParkPlan CreateParkPlan(NodeContext ctx);

    protected readonly record struct ParkPlan(WakeCondition Condition, string MarkerValue, DateTime? Deadline);

    public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        _ = ct;
        Guid nodeId = ctx.Node.NodeId;
        IReadOnlyDictionary<string, JsonElement> state = ctx.Branch.LocalState;

        // Resume visit: this node's own marker from its first visit is present. Every exit clears the
        // marker, the deadline and the wake, so a later await on this branch - or this node on its next
        // loop lap - parks afresh instead of mistaking them for its own.
        if (ParkMarker.TryReadOwn(state, ParkedKey, nodeId, out _))
        {
            IReadOnlyCollection<string> removeKeys = [ParkedKey, DeadlineKey, .. ParkMarker.WakeKeys];

            // The resumer stores the inbound payload under "__wake" when an event arrives; its
            // presence distinguishes an event wake (continue via "default", promoting the payload)
            // from a TTL timeout (a timer wake carries no payload).
            switch (ParkMarker.ReadWake(state, nodeId, out JsonElement wake))
            {
                case ParkMarker.WakeOwner.This:
                    return Continue(DefaultPort, PromoteWake(wake, WakePayloadKey), removeKeys);

                case ParkMarker.WakeOwner.Other:
                    // A wake delivered for another node's bookmark is not this node's to consume: keep
                    // waiting on the same condition, for whatever is left of the original deadline.
                    return RePark(ctx, state);
            }

            if (TryReadDeadline(state, out DateTime deadline) && Clock.GetUtcNow().UtcDateTime >= deadline)
            {
                return Continue(DefaultTimeoutPort, new Dictionary<string, JsonElement>(), removeKeys);
            }

            return Continue(DefaultPort, new Dictionary<string, JsonElement>(), removeKeys);
        }

        // First visit: park on the derived executor's wake condition.
        ParkPlan plan = CreateParkPlan(ctx);

        var patch = new Dictionary<string, JsonElement>
        {
            [ParkedKey] = ParkMarker.Create(nodeId, plan.MarkerValue)
        };
        if (plan.Deadline is DateTime deadlineValue)
        {
            patch[DeadlineKey] = JsonSerializer.SerializeToElement(deadlineValue.ToString("O"));
        }

        return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.WaitForBookmark(plan.Condition, patch));
    }

    private Task<NodeExecutionResult> RePark(NodeContext ctx, IReadOnlyDictionary<string, JsonElement> state)
    {
        WakeCondition condition = CreateParkPlan(ctx).Condition;
        if (condition.Ttl is not null && TryReadDeadline(state, out DateTime deadline))
        {
            TimeSpan remaining = deadline - Clock.GetUtcNow().UtcDateTime;
            condition = condition with { Ttl = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero };
        }

        // The marker and deadline already on the branch stay as they are.
        return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.WaitForBookmark(condition, new Dictionary<string, JsonElement>()));
    }

    private bool TryReadDeadline(IReadOnlyDictionary<string, JsonElement> state, out DateTime deadline)
    {
        deadline = default;
        return state.TryGetValue(DeadlineKey, out JsonElement deadlineElement)
            && deadlineElement.ValueKind == JsonValueKind.String
            && DateTime.TryParse(deadlineElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out deadline);
    }

    private static Task<NodeExecutionResult> Continue(string port, IReadOnlyDictionary<string, JsonElement> patch, IReadOnlyCollection<string> removeKeys)
    {
        return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Continue(port, patch, removeKeys));
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

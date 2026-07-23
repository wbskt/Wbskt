using System.Globalization;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

/// <summary>
/// Shared machine for control nodes that park a branch until an inbound event arrives on some
/// correlation channel, or until a TTL elapses. First visit registers a bookmark on the wake
/// condition supplied by the derived executor and writes a local-state marker; the engine
/// re-executes the same node on resume, where the presence of the marker distinguishes a resume
/// from a first visit. A resume leaves via the "default" port (promoting any inbound payload) when
/// the resumer delivered a wake body, and via the "timeout" port once the deadline has passed.
/// Derived executors differ only in which wake condition they park on and the local-state key names
/// they use - see <see cref="AwaitSignalNodeExecutor"/> and <see cref="WaitForHttpNodeExecutor"/>.
/// </summary>
internal abstract class AwaitInboundNodeExecutor : INodeExecutor
{
    // Reserved key the resumer writes the inbound payload under; shared across all await executors.
    private const string WakeKey = "__wake";
    private const string DefaultPort = "default";
    protected const string DefaultTimeoutPort = "timeout"; // edges map the port to the target node

    protected AwaitInboundNodeExecutor(IClock clock)
    {
        Clock = clock;
    }

    protected IClock Clock { get; }

    public abstract string Kind { get; }

    /// <summary>Local-state key whose presence marks that this node has already parked.</summary>
    protected abstract string ParkedKey { get; }

    /// <summary>Local-state key holding the round-trippable deadline timestamp (absent when no TTL).</summary>
    protected abstract string DeadlineKey { get; }

    /// <summary>Key the inbound payload is promoted under when the branch resumes via a wake.</summary>
    protected abstract string WakePayloadKey { get; }

    /// <summary>
    /// Builds the first-visit park plan: the wake condition to register, the marker value stored
    /// under <see cref="ParkedKey"/>, and the absolute deadline (null parks with no TTL).
    /// </summary>
    protected abstract ParkPlan CreateParkPlan(NodeContext ctx);

    protected readonly record struct ParkPlan(WakeCondition Condition, string MarkerValue, DateTime? Deadline);

    public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        _ = ct;

        // Resume visit: the marker from the first visit is present.
        if (ctx.Branch.LocalState.ContainsKey(ParkedKey))
        {
            // The resumer stores the inbound payload under "__wake" when an event arrives; its
            // presence distinguishes an event wake (continue via "default", promoting the payload)
            // from a TTL timeout (a timer wake carries no payload).
            if (ctx.Branch.LocalState.TryGetValue(WakeKey, out JsonElement wake))
            {
                return Continue(DefaultPort, PromoteWake(wake, WakePayloadKey));
            }

            if (ctx.Branch.LocalState.TryGetValue(DeadlineKey, out JsonElement deadlineElement)
                && deadlineElement.ValueKind == JsonValueKind.String
                && DateTime.TryParse(deadlineElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime deadline)
                && Clock.UtcNow >= deadline)
            {
                return Continue(DefaultTimeoutPort);
            }

            return Continue(DefaultPort);
        }

        // First visit: park on the derived executor's wake condition.
        ParkPlan plan = CreateParkPlan(ctx);

        var patch = new Dictionary<string, JsonElement>
        {
            [ParkedKey] = JsonSerializer.SerializeToElement(plan.MarkerValue)
        };
        if (plan.Deadline is DateTime deadlineValue)
        {
            patch[DeadlineKey] = JsonSerializer.SerializeToElement(deadlineValue.ToString("O"));
        }

        return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.WaitForBookmark(plan.Condition, patch));
    }

    private static Task<NodeExecutionResult> Continue(string port)
    {
        return Continue(port, new Dictionary<string, JsonElement>());
    }

    private static Task<NodeExecutionResult> Continue(string port, IReadOnlyDictionary<string, JsonElement> patch)
    {
        return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Continue(port, patch));
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

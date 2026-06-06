using System.Globalization;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

/// <summary>
/// Parks a branch until a named signal arrives for this run (the inbound "signal" channel),
/// or until an optional TTL elapses. The bookmark match key is
/// "signal:{name}:{scope}", where scope defaults to the run's RefId — exactly the
/// correlation key CorrelationKeyResolver produces for an inbound signal, so the two match.
/// First-visit vs resume is distinguished by a local-state marker (the engine re-executes
/// the same node on resume); on a TTL resume it leaves via the timeout port.
/// </summary>
public sealed class AwaitSignalNodeExecutor(IClock clock) : INodeExecutor
{
    internal const string ParkedKey = "__await_signal";
    internal const string DeadlineKey = "__await_signal_until";
    internal const string WakeKey = "__wake";
    internal const string SignalPayloadKey = "signalPayload";
    private const string DefaultPort = "default";
    private const string DefaultTimeoutPort = "timeout";

    public string Kind => NodeKind.ControlAwaitSignal;

    public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        _ = ct;
        var node = (AwaitSignalNode)ctx.Node;
        AwaitSignalConfig config = node.Config
            ?? throw new InvalidOperationException($"AwaitSignal node {node.NodeId} is missing config.");

        // Resume visit: the marker from the first visit is present.
        if (ctx.Branch.LocalState.ContainsKey(ParkedKey))
        {
            // The resumer stores the inbound payload under "__wake" when a signal arrives; its
            // presence distinguishes a signal wake (continue via "default", promoting the payload
            // under "signalPayload") from a TTL timeout (a timer wake carries no payload).
            if (ctx.Branch.LocalState.TryGetValue(WakeKey, out JsonElement wake))
            {
                return Continue(DefaultPort, PromoteWake(wake, SignalPayloadKey));
            }

            if (ctx.Branch.LocalState.TryGetValue(DeadlineKey, out JsonElement deadlineElement)
                && deadlineElement.ValueKind == JsonValueKind.String
                && DateTime.TryParse(deadlineElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime deadline)
                && clock.UtcNow >= deadline)
            {
                string timeoutPort = string.IsNullOrWhiteSpace(config.OnTimeout) ? DefaultTimeoutPort : config.OnTimeout;
                return Continue(timeoutPort);
            }

            return Continue(DefaultPort);
        }

        // First visit: park on a signal scoped to this run (or an explicit correlation).
        string scope = string.IsNullOrWhiteSpace(config.Correlation) ? ctx.Branch.RunRefId.ToString() : config.Correlation;
        var condition = new SignalWakeCondition(config.SignalName, scope) { Ttl = config.Ttl };

        var patch = new Dictionary<string, JsonElement>
        {
            [ParkedKey] = JsonSerializer.SerializeToElement(config.SignalName)
        };
        if (config.Ttl is TimeSpan ttl)
        {
            patch[DeadlineKey] = JsonSerializer.SerializeToElement((clock.UtcNow + ttl).ToString("O"));
        }

        return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.WaitForBookmark(condition, patch));
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

using System.Globalization;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

/// <summary>
/// Parks a branch until an external system POSTs to the wake callback (the inbound "http-wake"
/// channel), or until the TTL elapses. The bookmark match key is "http-wake:{token}", matching
/// CorrelationKeyResolver. For testability the token is the run's RefId, so the callback is
/// POST /api/inbound/wake/{runRefId}; production would mint a random secret per park and deliver
/// it in an outbound callback URL. First-visit vs resume is distinguished by a local-state marker;
/// a TTL resume leaves via the timeout port.
/// </summary>
public sealed class WaitForHttpNodeExecutor(IClock clock) : INodeExecutor
{
    internal const string ParkedKey = "__http_wait";
    internal const string DeadlineKey = "__http_deadline";
    private const string DefaultPort = "default";
    private const string DefaultTimeoutPort = "timeout";

    public string Kind => NodeKind.ControlWaitForHttp;

    public Task<NodeExecutionResult> ExecuteAsync(NodeContext ctx, CancellationToken ct)
    {
        _ = ct;
        var node = (WaitForHttpNode)ctx.Node;
        WaitForHttpConfig config = node.Config
            ?? throw new InvalidOperationException($"WaitForHttp node {node.NodeId} is missing config.");

        // Resume visit.
        if (ctx.Branch.LocalState.ContainsKey(ParkedKey))
        {
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

        // First visit: park on an http-wake callback scoped to this run.
        string token = ctx.Branch.RunRefId.ToString();
        var condition = new HttpWakeCondition(token) { Ttl = config.Ttl };

        var patch = new Dictionary<string, JsonElement>
        {
            [ParkedKey] = JsonSerializer.SerializeToElement(token),
            [DeadlineKey] = JsonSerializer.SerializeToElement((clock.UtcNow + config.Ttl).ToString("O"))
        };

        return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.WaitForBookmark(condition, patch));
    }

    private static Task<NodeExecutionResult> Continue(string port)
    {
        return Task.FromResult<NodeExecutionResult>(
            new NodeExecutionResult.Continue(port, new Dictionary<string, JsonElement>()));
    }
}

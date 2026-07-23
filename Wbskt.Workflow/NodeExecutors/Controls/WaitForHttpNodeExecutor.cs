using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

/// <summary>
/// Parks a branch until an external system POSTs to the wake callback (the inbound "http-wake"
/// channel), or until the TTL elapses. The bookmark match key is "http-wake:{token}", matching
/// CorrelationKeyResolver. The token is author-defined (WaitForHttpConfig.Token) so the callback
/// URL is known at design time and can be handed to the external caller as a shared secret; when
/// the author leaves it unset it defaults to the run's RefId, which scopes the wake per-run but is
/// not a secret (it also appears in run-list APIs and logs). See <see cref="AwaitInboundNodeExecutor"/>
/// for the shared park/resume/timeout machine.
/// </summary>
internal sealed class WaitForHttpNodeExecutor(IClock clock) : AwaitInboundNodeExecutor(clock)
{
    public override string Kind => NodeKind.ControlWaitForHttp;

    protected override string ParkedKey => "__http_wait";
    protected override string DeadlineKey => "__http_deadline";
    protected override string WakePayloadKey => "wakePayload";

    protected override ParkPlan CreateParkPlan(NodeContext ctx)
    {
        var node = (WaitForHttpNode)ctx.Node;
        WaitForHttpConfig config = node.Config
            ?? throw new InvalidOperationException($"WaitForHttp node {node.NodeId} is missing config.");

        // The token is a required author-defined secret: it is the only thing gating the public wake
        // callback, so there is no fallback to the run's RefId (which is not secret - it appears in
        // run-list APIs and logs). WorkflowValidator rejects a WaitForHttp node without a strong token
        // at publish time; this guard defends against a definition that bypassed validation.
        if (string.IsNullOrWhiteSpace(config.Token))
        {
            throw new InvalidOperationException($"WaitForHttp node {node.NodeId} requires a 'token'.");
        }

        string token = config.Token;
        var condition = new HttpWakeCondition(token)
        {
            Ttl = config.Ttl,
            TtlPort = DefaultTimeoutPort
        };

        return new ParkPlan(condition, token, Clock.UtcNow + config.Ttl);
    }
}

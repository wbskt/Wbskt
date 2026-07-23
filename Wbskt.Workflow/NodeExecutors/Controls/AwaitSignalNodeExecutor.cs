using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

/// <summary>
/// Parks a branch until a named signal arrives for this run (the inbound "signal" channel), or
/// until an optional TTL elapses. The bookmark match key is "signal:{name}:{scope}", where scope
/// defaults to the run's RefId - exactly the correlation key CorrelationKeyResolver produces for an
/// inbound signal, so the two match. See <see cref="AwaitInboundNodeExecutor"/> for the shared
/// park/resume/timeout machine.
/// </summary>
internal sealed class AwaitSignalNodeExecutor(IClock clock) : AwaitInboundNodeExecutor(clock)
{
    public override string Kind => NodeKind.ControlAwaitSignal;

    protected override string ParkedKey => "__await_signal";
    protected override string DeadlineKey => "__await_signal_until";
    protected override string WakePayloadKey => "signalPayload";

    protected override ParkPlan CreateParkPlan(NodeContext ctx)
    {
        var node = (AwaitSignalNode)ctx.Node;
        AwaitSignalConfig config = node.Config
            ?? throw new InvalidOperationException($"AwaitSignal node {node.NodeId} is missing config.");

        // Park on a signal scoped to this run (or an explicit correlation).
        string scope = string.IsNullOrWhiteSpace(config.Correlation) ? ctx.Branch.RunRefId.ToString() : config.Correlation;
        var condition = new SignalWakeCondition(config.SignalName, scope)
        {
            Ttl = config.Ttl,
            TtlPort = DefaultTimeoutPort
        };

        DateTime? deadline = config.Ttl is TimeSpan ttl ? Clock.UtcNow + ttl : null;
        return new ParkPlan(condition, config.SignalName, deadline);
    }
}

using Wbskt.Workflow.Abstraction.Models.Bookmarks;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Controls;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Runtime;

namespace Wbskt.Workflow.NodeExecutors.Controls;

/// <summary>
/// Parks a branch until a named signal arrives for this run (the inbound "signal" channel), or
/// until an optional TTL elapses. The bookmark match key is
/// <see cref="Channels.SignalChannel.Key"/> of the name and a scope that defaults to the run's RefId,
/// the same key the channel computes for an inbound signal, so the two match. An explicit correlation
/// has its <c>{{ }}</c> placeholders filled in from the branch first, so each run parks on its own
/// value rather than on the placeholder text every run shares. See <see cref="AwaitInboundNodeExecutor"/>
/// for the shared park/resume/timeout machine.
/// </summary>
internal sealed class AwaitSignalNodeExecutor(TimeProvider clock, ActionTemplates templates) : AwaitInboundNodeExecutor(clock)
{
    private const string CorrelationMissingCode = "SIGNAL_CORRELATION_MISSING";

    public override string Kind => NodeKind.ControlAwaitSignal;

    protected override string ParkedKey => BranchStateKeys.AwaitSignal;
    protected override string DeadlineKey => BranchStateKeys.AwaitSignalUntil;
    protected override string WakePayloadKey => "signalPayload";

    // A placeholder with no value would leave a blank in the key, and runs that differ only in that
    // value would share one bookmark and answer each other's signals, so the step fails instead.
    protected override async Task<NodeExecutionResult> FirstVisitAsync(NodeContext ctx, CancellationToken ct)
    {
        AwaitSignalConfig config = ConfigOf(ctx);
        (string scope, string? problem) = await ResolveScopeAsync(ctx, config, ct);
        return problem is null
            ? Park(ctx, Plan(config, scope))
            : new NodeExecutionResult.Fail(CorrelationMissingCode, problem, false, null);
    }

    // Reached on a resume only, after the first visit parked: the branch has not changed since, so
    // the correlation fills in to the same scope it parked on.
    protected override async ValueTask<ParkPlan> CreateParkPlanAsync(NodeContext ctx, CancellationToken ct)
    {
        AwaitSignalConfig config = ConfigOf(ctx);
        (string scope, _) = await ResolveScopeAsync(ctx, config, ct);
        return Plan(config, scope);
    }

    private static AwaitSignalConfig ConfigOf(NodeContext ctx)
    {
        var node = (AwaitSignalNode)ctx.Node;
        return node.Config ?? throw new InvalidOperationException($"AwaitSignal node {node.NodeId} is missing config.");
    }

    private async Task<(string Scope, string? Problem)> ResolveScopeAsync(NodeContext ctx, AwaitSignalConfig config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.Correlation))
        {
            return (ctx.Branch.RunRefId.ToString(), null);
        }

        (string text, IReadOnlyList<string> missing) = await templates.FillLineAsync(ctx, config.Correlation, ct);
        if (missing.Count > 0)
        {
            string names = string.Join(", ", missing.Select(reference => $"'{{{{{reference}}}}}'"));
            return (text, $"The correlation '{config.Correlation}' has no value for {names}, so there is nothing to wait on.");
        }

        return string.IsNullOrWhiteSpace(text)
            ? (text, $"The correlation '{config.Correlation}' fills in to blank text, so there is nothing to wait on.")
            : (text, null);
    }

    private ParkPlan Plan(AwaitSignalConfig config, string scope)
    {
        var condition = new SignalWakeCondition(config.SignalName, scope)
        {
            Ttl = config.Ttl,
            TtlPort = DefaultTimeoutPort
        };

        DateTime? deadline = config.Ttl is TimeSpan ttl ? Clock.GetUtcNow().UtcDateTime + ttl : null;
        return new ParkPlan(condition, config.SignalName, deadline);
    }
}

using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class PendingTriggerEventBacklogReaper(
    TimeProvider clock,
    ILeadershipGate leadership,
    IServiceScopeFactory scopeFactory,
    ILogger<PendingTriggerEventBacklogReaper> logger,
    IOptions<WorkflowEngineOptions> options) : LeaderPeriodicWorker(leadership, scopeFactory, logger)
{
    private const int BatchSize = 1000;
    private readonly TimeSpan _pendingTtl = options.Value.PendingTriggerEventTtl;

    protected override string Name => "Pending trigger backlog reaper";

    protected override TimeSpan Interval { get; } = options.Value.PendingTriggerEventBacklogInterval;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var pendingTriggerEventProvider = services.GetRequiredService<IPendingTriggerEventProvider>();
        DateTime cutoffUtc = clock.GetUtcNow().UtcDateTime - _pendingTtl;
        await pendingTriggerEventProvider.DeleteExpiredAsync(cutoffUtc, BatchSize, ct);
    }
}

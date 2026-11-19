using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

/// <summary>
/// Drops queued trigger events older than <see cref="WorkflowEngineOptions.PendingTriggerEventTtl"/>, and
/// drains each queue no finalizer will: one with no run going on its key, left when the engine stopped
/// between a run's end and its drain, or when a replay's start failed (#962).
/// </summary>
public sealed class PendingTriggerEventBacklogReaper(
    TimeProvider clock,
    ILeadershipGate leadership,
    IServiceScopeFactory scopeFactory,
    ILogger<PendingTriggerEventBacklogReaper> logger,
    IOptions<WorkflowEngineOptions> options) : LeaderPeriodicWorker(leadership, scopeFactory, logger, clock)
{
    private readonly int _batchSize = 1000;
    private readonly int _idleKeyBatchSize = 100;
    private readonly TimeSpan _pendingTtl = options.Value.PendingTriggerEventTtl;

    protected override string Name => "Pending trigger backlog reaper";

    protected override TimeSpan Interval { get; } = options.Value.PendingTriggerEventBacklogInterval;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var pendingTriggerEventProvider = services.GetRequiredService<IPendingTriggerEventProvider>();
        DateTime cutoffUtc = clock.GetUtcNow().UtcDateTime - _pendingTtl;
        await pendingTriggerEventProvider.DeleteExpiredAsync(cutoffUtc, _batchSize, ct);

        var drainer = services.GetRequiredService<IPendingTriggerEventDrainer>();
        IReadOnlyCollection<PendingTriggerEventKey> idleKeys = await pendingTriggerEventProvider.GetIdleKeysAsync(_idleKeyBatchSize, ct);
        foreach (PendingTriggerEventKey key in idleKeys)
        {
            try
            {
                await drainer.DrainAsync(key.WorkflowRefId, key.TriggerNodeId, key.CorrelationKey, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Logger.LogError(ex, "Could not start the event queued for workflow {WorkflowRefId} trigger {TriggerNodeId}; trying again next pass.", key.WorkflowRefId, key.TriggerNodeId);
            }
        }

        if (idleKeys.Count > 0)
        {
            Logger.LogInformation("Drained {Count} trigger queue(s) that had no run going.", idleKeys.Count);
        }
    }
}

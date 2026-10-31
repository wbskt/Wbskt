using Wbskt.EventBus.Abstractions;
using Wbskt.Workflow.Engine.Host.InboundAdapters;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

/// <summary>
/// Publishes the coalesced ceiling refusals (<see cref="RunRefusalCoalescer"/>) as their windows end,
/// and whatever is left when the host stops. Runs on every replica, leader or not: each one refuses
/// triggers of its own.
/// </summary>
internal sealed class RunRefusalPublisher(RunRefusalCoalescer coalescer, IEventBus eventBus, TimeProvider clock, ILogger<RunRefusalPublisher> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Tick, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await PublishAsync(all: false, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        await PublishAsync(all: true, CancellationToken.None);
    }

    internal async Task PublishAsync(bool all, CancellationToken ct)
    {
        foreach (var warning in coalescer.TakeDue(clock.GetUtcNow().UtcDateTime, all))
        {
            try
            {
                await eventBus.PublishAsync(warning, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not publish the ceiling warning for workflow {WorkflowRefId} ({Count} refused)", warning.WorkflowRefId, warning.RefusedCount);
            }
        }
    }
}

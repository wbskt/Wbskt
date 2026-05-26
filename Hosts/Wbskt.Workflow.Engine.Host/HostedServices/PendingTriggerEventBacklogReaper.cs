using Microsoft.Extensions.Hosting;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class PendingTriggerEventBacklogReaper : BackgroundService
{
    private const string LeaseName = "pending-trigger-backlog-reaper";
    private const int BatchSize = 1000;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PendingTtl = TimeSpan.FromHours(24);
    private readonly IClock _clock;
    private readonly ILeaseHolder _leaseHolder;
    private readonly IPendingTriggerEventProvider _pendingTriggerEventProvider;
    private readonly ILogger<PendingTriggerEventBacklogReaper> _logger;

    public PendingTriggerEventBacklogReaper(
        IClock clock,
        ILeaseHolder leaseHolder,
        IPendingTriggerEventProvider pendingTriggerEventProvider,
        ILogger<PendingTriggerEventBacklogReaper> logger)
    {
        _clock = clock;
        _leaseHolder = leaseHolder;
        _pendingTriggerEventProvider = pendingTriggerEventProvider;
        _logger = logger;
    }

    public async Task ProcessBacklogAsync(CancellationToken ct)
    {
        if (!await _leaseHolder.IsHeldAsync(LeaseName, ct))
        {
            return;
        }

        DateTime cutoffUtc = _clock.UtcNow - PendingTtl;
        await _pendingTriggerEventProvider.DeleteExpiredAsync(cutoffUtc, BatchSize, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ExecuteGuardedAsync(stoppingToken);

        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ExecuteGuardedAsync(stoppingToken);
        }
    }

    private async Task ExecuteGuardedAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ProcessBacklogAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pending trigger backlog reaper tick failed.");
        }
    }
}

using Microsoft.Extensions.Hosting;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class HistoryRetentionGc : BackgroundService
{
    private const string LeaseName = "history-retention-gc";
    private const int BatchSize = 5000;
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(30);
    private readonly IClock _clock;
    private readonly ILeaseHolder _leaseHolder;
    private readonly IHistoryEventProvider _historyEventProvider;
    private readonly ILogger<HistoryRetentionGc> _logger;

    public HistoryRetentionGc(
        IClock clock,
        ILeaseHolder leaseHolder,
        IHistoryEventProvider historyEventProvider,
        ILogger<HistoryRetentionGc> logger)
    {
        _clock = clock;
        _leaseHolder = leaseHolder;
        _historyEventProvider = historyEventProvider;
        _logger = logger;
    }

    public async Task ProcessRetentionAsync(CancellationToken ct)
    {
        if (!await _leaseHolder.IsHeldAsync(LeaseName, ct))
        {
            return;
        }

        DateTime cutoffUtc = _clock.UtcNow - RetentionWindow;
        while (true)
        {
            int deleted = await _historyEventProvider.DeleteForRetiredRunsAsync(cutoffUtc, BatchSize, ct);
            if (deleted < BatchSize)
            {
                return;
            }
        }
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
            await ProcessRetentionAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "History retention GC tick failed.");
        }
    }
}

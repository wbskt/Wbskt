using Microsoft.Extensions.Hosting;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class RunReaper : BackgroundService
{
    private const string LeaseName = "run-reaper";
    private const int BatchSize = 100;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StuckThreshold = TimeSpan.FromMinutes(30);
    private readonly IClock _clock;
    private readonly ILeaseHolder _leaseHolder;
    private readonly IRunProvider _runProvider;
    private readonly IRunCancellationService _runCancellationService;
    private readonly ILogger<RunReaper> _logger;

    public RunReaper(
        IClock clock,
        ILeaseHolder leaseHolder,
        IRunProvider runProvider,
        IRunCancellationService runCancellationService,
        ILogger<RunReaper> logger)
    {
        _clock = clock;
        _leaseHolder = leaseHolder;
        _runProvider = runProvider;
        _runCancellationService = runCancellationService;
        _logger = logger;
    }

    public async Task ProcessStuckRunsAsync(CancellationToken ct)
    {
        if (!await _leaseHolder.IsHeldAsync(LeaseName, ct))
        {
            return;
        }

        DateTime cutoffUtc = _clock.UtcNow - StuckThreshold;
        IReadOnlyCollection<RunRow> stuckRuns = await _runProvider.GetStuckRunsAsync(cutoffUtc, BatchSize, ct);
        foreach (RunRow run in stuckRuns)
        {
            await _runCancellationService.RequestCancellationAsync(run.Id, "REAPER_TIMEOUT", ct);
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
            await ProcessStuckRunsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run reaper tick failed.");
        }
    }
}

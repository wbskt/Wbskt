using Microsoft.Extensions.Hosting;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Telemetry;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class MetricsExporter : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    private readonly IRunProvider _runProvider;
    private readonly IRunCountersProvider _runCountersProvider;
    private readonly IBookmarkProvider _bookmarkProvider;
    private readonly IPendingTriggerEventProvider _pendingTriggerEventProvider;
    private readonly WorkflowMetrics _workflowMetrics;
    private readonly ILogger<MetricsExporter> _logger;

    public MetricsExporter(
        IRunProvider runProvider,
        IRunCountersProvider runCountersProvider,
        IBookmarkProvider bookmarkProvider,
        IPendingTriggerEventProvider pendingTriggerEventProvider,
        WorkflowMetrics workflowMetrics,
        ILogger<MetricsExporter> logger)
    {
        _runProvider = runProvider;
        _runCountersProvider = runCountersProvider;
        _bookmarkProvider = bookmarkProvider;
        _pendingTriggerEventProvider = pendingTriggerEventProvider;
        _workflowMetrics = workflowMetrics;
        _logger = logger;
    }

    public async Task ProcessMetricsAsync(CancellationToken ct)
    {
        long activeRuns = await _runProvider.CountByStatusAsync("Running", ct);
        long activeBranches = await _runCountersProvider.SumActiveBranchesAsync(ct);
        long parkedBookmarks = await _bookmarkProvider.CountAsync(ct);
        long pendingTriggerDepth = await _pendingTriggerEventProvider.CountAllAsync(ct);

        _workflowMetrics.UpdateSnapshot(activeRuns, activeBranches, parkedBookmarks, pendingTriggerDepth);

        _logger.LogInformation(
            "Workflow metrics exported: ActiveRuns={ActiveRuns}, ActiveBranches={ActiveBranches}, ParkedBookmarks={ParkedBookmarks}, PendingTriggerDepth={PendingTriggerDepth}",
            activeRuns,
            activeBranches,
            parkedBookmarks,
            pendingTriggerDepth);
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
            await ProcessMetricsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Metrics exporter tick failed.");
        }
    }
}

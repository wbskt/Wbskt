using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Telemetry;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class MetricsExporter : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkflowMetrics _workflowMetrics;
    private readonly ILogger<MetricsExporter> _logger;
    private readonly TimeSpan _pollInterval;

    [ActivatorUtilitiesConstructor]
    public MetricsExporter(
        IServiceScopeFactory scopeFactory,
        WorkflowMetrics workflowMetrics,
        ILogger<MetricsExporter> logger,
        IOptions<WorkflowEngineOptions> options)
        : this(scopeFactory, workflowMetrics, logger, options.Value.MetricsExportInterval)
    {
    }

    internal MetricsExporter(
        IRunProvider runProvider,
        IRunCountersProvider runCountersProvider,
        IBookmarkProvider bookmarkProvider,
        IPendingTriggerEventProvider pendingTriggerEventProvider,
        WorkflowMetrics workflowMetrics,
        ILogger<MetricsExporter> logger,
        TimeSpan? pollInterval = null)
        : this(new StaticScopeFactory(runProvider, runCountersProvider, bookmarkProvider, pendingTriggerEventProvider), workflowMetrics, logger, pollInterval)
    {
    }

    private MetricsExporter(
        IServiceScopeFactory scopeFactory,
        WorkflowMetrics workflowMetrics,
        ILogger<MetricsExporter> logger,
        TimeSpan? pollInterval)
    {
        _scopeFactory = scopeFactory;
        _workflowMetrics = workflowMetrics;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(15);
    }

    public async Task ProcessMetricsAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        var runProvider = scope.ServiceProvider.GetRequiredService<IRunProvider>();
        var runCountersProvider = scope.ServiceProvider.GetRequiredService<IRunCountersProvider>();
        var bookmarkProvider = scope.ServiceProvider.GetRequiredService<IBookmarkProvider>();
        var pendingTriggerEventProvider = scope.ServiceProvider.GetRequiredService<IPendingTriggerEventProvider>();
        long activeRuns = await runProvider.CountByStatusAsync("Running", ct);
        long activeBranches = await runCountersProvider.SumActiveBranchesAsync(ct);
        long parkedBookmarks = await bookmarkProvider.CountAsync(ct);
        long pendingTriggerDepth = await pendingTriggerEventProvider.CountAllAsync(ct);

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

        using var timer = new PeriodicTimer(_pollInterval);
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

    private sealed class StaticScopeFactory(
        IRunProvider runProvider,
        IRunCountersProvider runCountersProvider,
        IBookmarkProvider bookmarkProvider,
        IPendingTriggerEventProvider pendingTriggerEventProvider) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            return new StaticServiceScope(runProvider, runCountersProvider, bookmarkProvider, pendingTriggerEventProvider);
        }
    }

    private sealed class StaticServiceScope(
        IRunProvider runProvider,
        IRunCountersProvider runCountersProvider,
        IBookmarkProvider bookmarkProvider,
        IPendingTriggerEventProvider pendingTriggerEventProvider) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider { get; } = new StaticServiceProvider(runProvider, runCountersProvider, bookmarkProvider, pendingTriggerEventProvider);

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StaticServiceProvider(
        IRunProvider runProvider,
        IRunCountersProvider runCountersProvider,
        IBookmarkProvider bookmarkProvider,
        IPendingTriggerEventProvider pendingTriggerEventProvider) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IRunProvider))
            {
                return runProvider;
            }

            if (serviceType == typeof(IRunCountersProvider))
            {
                return runCountersProvider;
            }

            if (serviceType == typeof(IBookmarkProvider))
            {
                return bookmarkProvider;
            }

            if (serviceType == typeof(IPendingTriggerEventProvider))
            {
                return pendingTriggerEventProvider;
            }

            return null;
        }
    }
}

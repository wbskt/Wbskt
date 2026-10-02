using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Runtime;
using Wbskt.Workflow.Telemetry;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

internal sealed class MetricsExporter : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly WorkflowMetrics _workflowMetrics;
    private readonly ChannelRunDispatcher _dispatcher;
    private readonly ILogger<MetricsExporter> _logger;
    private readonly TimeSpan _pollInterval;

    [ActivatorUtilitiesConstructor]
    public MetricsExporter(
        IServiceScopeFactory scopeFactory,
        WorkflowMetrics workflowMetrics,
        ChannelRunDispatcher dispatcher,
        ILogger<MetricsExporter> logger,
        IOptions<WorkflowEngineOptions> options)
        : this(scopeFactory, workflowMetrics, dispatcher, logger, options.Value.MetricsExportInterval)
    {
    }

    internal MetricsExporter(
        IRunProvider runProvider,
        IRunCountersProvider runCountersProvider,
        IBookmarkProvider bookmarkProvider,
        IPendingTriggerEventProvider pendingTriggerEventProvider,
        WorkflowMetrics workflowMetrics,
        ChannelRunDispatcher dispatcher,
        ILogger<MetricsExporter> logger,
        TimeSpan? pollInterval = null)
        : this(new StaticScopeFactory(runProvider, runCountersProvider, bookmarkProvider, pendingTriggerEventProvider), workflowMetrics, dispatcher, logger, pollInterval)
    {
    }

    private MetricsExporter(
        IServiceScopeFactory scopeFactory,
        WorkflowMetrics workflowMetrics,
        ChannelRunDispatcher dispatcher,
        ILogger<MetricsExporter> logger,
        TimeSpan? pollInterval)
    {
        _scopeFactory = scopeFactory;
        _workflowMetrics = workflowMetrics;
        _dispatcher = dispatcher;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(15);
    }

    public async Task ProcessMetricsAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        var runCountersProvider = scope.ServiceProvider.GetRequiredService<IRunCountersProvider>();
        var bookmarkProvider = scope.ServiceProvider.GetRequiredService<IBookmarkProvider>();
        var pendingTriggerEventProvider = scope.ServiceProvider.GetRequiredService<IPendingTriggerEventProvider>();
        
        long activeBranches = await runCountersProvider.SumActiveBranchesAsync(ct);
        long pendingTriggerDepth = await pendingTriggerEventProvider.CountAllAsync(ct);
        long queueDepth = _dispatcher.Count;
        IReadOnlyDictionary<string, long> bookmarksByWakeKind = await bookmarkProvider.CountGroupedByWakeKindAsync(ct);

        _workflowMetrics.UpdateSnapshot(activeBranches, queueDepth, pendingTriggerDepth, bookmarksByWakeKind);

        // A minute of grace: the poller claims on its own interval, so a bookmark a few seconds past
        // due is normal; one a minute past due is not.
        long overdueBookmarks = await bookmarkProvider.CountOverdueAsync(DateTime.UtcNow.AddMinutes(-1), ct);
        _workflowMetrics.UpdateOverdueBookmarks(overdueBookmarks);

        _logger.LogInformation(
            "Workflow metrics exported: ActiveBranches={ActiveBranches}, DispatcherQueueDepth={QueueDepth}, PendingTriggerDepth={PendingTriggerDepth}, BookmarksCount={BookmarksCount}, OverdueBookmarks={OverdueBookmarks}",
            activeBranches,
            queueDepth,
            pendingTriggerDepth,
            bookmarksByWakeKind.Values.Sum(),
            overdueBookmarks);
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

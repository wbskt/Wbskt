using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class HistoryRetentionGc : BackgroundService
{
    private const string LeaseName = "history-retention-gc";
    private const int BatchSize = 5000;
    private readonly IClock _clock;
    private readonly ILeaseHolder _leaseHolder;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HistoryRetentionGc> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _retentionWindow;
    private readonly TimeSpan _elevatedRetentionWindow;

    [ActivatorUtilitiesConstructor]
    public HistoryRetentionGc(
        IClock clock,
        ILeaseHolder leaseHolder,
        IServiceScopeFactory scopeFactory,
        ILogger<HistoryRetentionGc> logger,
        IOptions<WorkflowEngineOptions> options)
        : this(clock, leaseHolder, scopeFactory, logger, options.Value.HistoryRetentionInterval, options.Value.HistoryRetentionWindow, options.Value.HistoryRetentionWindowElevated)
    {
    }

    internal HistoryRetentionGc(
        IClock clock,
        ILeaseHolder leaseHolder,
        IHistoryEventProvider historyEventProvider,
        ILogger<HistoryRetentionGc> logger,
        TimeSpan? pollInterval = null,
        TimeSpan? retentionWindow = null,
        TimeSpan? elevatedRetentionWindow = null)
        : this(clock, leaseHolder, new StaticScopeFactory(historyEventProvider), logger, pollInterval, retentionWindow, elevatedRetentionWindow)
    {
    }

    private HistoryRetentionGc(
        IClock clock,
        ILeaseHolder leaseHolder,
        IServiceScopeFactory scopeFactory,
        ILogger<HistoryRetentionGc> logger,
        TimeSpan? pollInterval,
        TimeSpan? retentionWindow,
        TimeSpan? elevatedRetentionWindow = null)
    {
        _clock = clock;
        _leaseHolder = leaseHolder;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromHours(1);
        _retentionWindow = retentionWindow ?? TimeSpan.FromDays(30);
        _elevatedRetentionWindow = elevatedRetentionWindow ?? TimeSpan.FromDays(365);
    }

    public async Task ProcessRetentionAsync(CancellationToken ct)
    {
        if (!await _leaseHolder.IsHeldAsync(LeaseName, ct))
        {
            return;
        }

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        var historyEventProvider = scope.ServiceProvider.GetRequiredService<IHistoryEventProvider>();
        DateTime cutoffUtc = _clock.UtcNow - _retentionWindow;
        DateTime elevatedCutoffUtc = _clock.UtcNow - _elevatedRetentionWindow;
        while (true)
        {
            int deleted = await historyEventProvider.DeleteForRetiredRunsAsync(cutoffUtc, BatchSize, elevatedCutoffUtc, ct);
            if (deleted < BatchSize)
            {
                return;
            }
        }
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

    private sealed class StaticScopeFactory(IHistoryEventProvider historyEventProvider) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            return new StaticServiceScope(historyEventProvider);
        }
    }

    private sealed class StaticServiceScope(IHistoryEventProvider historyEventProvider) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider { get; } = new StaticServiceProvider(historyEventProvider);

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StaticServiceProvider(IHistoryEventProvider historyEventProvider) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(IHistoryEventProvider) ? historyEventProvider : null;
        }
    }
}

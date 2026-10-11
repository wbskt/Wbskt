using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class PendingTriggerEventBacklogReaper : BackgroundService
{
    private const int BatchSize = 1000;
    private readonly IClock _clock;
    private readonly ILeadershipGate _leadership;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingTriggerEventBacklogReaper> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _pendingTtl;

    [ActivatorUtilitiesConstructor]
    public PendingTriggerEventBacklogReaper(
        IClock clock,
        ILeadershipGate leadership,
        IServiceScopeFactory scopeFactory,
        ILogger<PendingTriggerEventBacklogReaper> logger,
        IOptions<WorkflowEngineOptions> options)
        : this(clock, leadership, scopeFactory, logger, options.Value.PendingTriggerEventBacklogInterval, options.Value.PendingTriggerEventTtl)
    {
    }

    internal PendingTriggerEventBacklogReaper(
        IClock clock,
        ILeadershipGate leadership,
        IPendingTriggerEventProvider pendingTriggerEventProvider,
        ILogger<PendingTriggerEventBacklogReaper> logger,
        TimeSpan? pollInterval = null,
        TimeSpan? pendingTtl = null)
        : this(clock, leadership, new StaticScopeFactory(pendingTriggerEventProvider), logger, pollInterval, pendingTtl)
    {
    }

    private PendingTriggerEventBacklogReaper(
        IClock clock,
        ILeadershipGate leadership,
        IServiceScopeFactory scopeFactory,
        ILogger<PendingTriggerEventBacklogReaper> logger,
        TimeSpan? pollInterval,
        TimeSpan? pendingTtl)
    {
        _clock = clock;
        _leadership = leadership;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromMinutes(10);
        _pendingTtl = pendingTtl ?? TimeSpan.FromHours(24);
    }

    public async Task ProcessBacklogAsync(CancellationToken ct)
    {
        if (!_leadership.IsActive)
        {
            return;
        }

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        var pendingTriggerEventProvider = scope.ServiceProvider.GetRequiredService<IPendingTriggerEventProvider>();
        DateTime cutoffUtc = _clock.UtcNow - _pendingTtl;
        await pendingTriggerEventProvider.DeleteExpiredAsync(cutoffUtc, BatchSize, ct);
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

    private sealed class StaticScopeFactory(IPendingTriggerEventProvider pendingTriggerEventProvider) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            return new StaticServiceScope(pendingTriggerEventProvider);
        }
    }

    private sealed class StaticServiceScope(IPendingTriggerEventProvider pendingTriggerEventProvider) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider { get; } = new StaticServiceProvider(pendingTriggerEventProvider);

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StaticServiceProvider(IPendingTriggerEventProvider pendingTriggerEventProvider) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(IPendingTriggerEventProvider) ? pendingTriggerEventProvider : null;
        }
    }
}

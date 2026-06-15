using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class RunReaper : BackgroundService
{
    private const string LeaseName = "run-reaper";
    private const int BatchSize = 100;
    private readonly IClock _clock;
    private readonly ILeaseHolder _leaseHolder;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RunReaper> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _stuckThreshold;

    [ActivatorUtilitiesConstructor]
    public RunReaper(
        IClock clock,
        ILeaseHolder leaseHolder,
        IServiceScopeFactory scopeFactory,
        ILogger<RunReaper> logger,
        IOptions<WorkflowEngineOptions> options)
        : this(clock, leaseHolder, scopeFactory, logger, options.Value.RunReaperInterval, options.Value.RunStuckThreshold)
    {
    }

    internal RunReaper(
        IClock clock,
        ILeaseHolder leaseHolder,
        IRunProvider runProvider,
        IRunCancellationService runCancellationService,
        ILogger<RunReaper> logger,
        TimeSpan? pollInterval = null,
        TimeSpan? stuckThreshold = null)
        : this(clock, leaseHolder, new StaticScopeFactory(runProvider, runCancellationService), logger, pollInterval, stuckThreshold)
    {
    }

    private RunReaper(
        IClock clock,
        ILeaseHolder leaseHolder,
        IServiceScopeFactory scopeFactory,
        ILogger<RunReaper> logger,
        TimeSpan? pollInterval,
        TimeSpan? stuckThreshold)
    {
        _clock = clock;
        _leaseHolder = leaseHolder;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromMinutes(5);
        _stuckThreshold = stuckThreshold ?? TimeSpan.FromMinutes(30);
    }

    public async Task ProcessStuckRunsAsync(CancellationToken ct)
    {
        if (!await _leaseHolder.IsHeldAsync(LeaseName, ct))
        {
            return;
        }

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        var runProvider = scope.ServiceProvider.GetRequiredService<IRunProvider>();
        var runCancellationService = scope.ServiceProvider.GetRequiredService<IRunCancellationService>();
        DateTime cutoffUtc = _clock.UtcNow - _stuckThreshold;
        IReadOnlyCollection<RunRow> stuckRuns = await runProvider.GetStuckRunsAsync(cutoffUtc, BatchSize, ct);
        foreach (RunRow run in stuckRuns)
        {
            await runCancellationService.RequestCancellationAsync(run.Id, "REAPER_TIMEOUT", ct);
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

    private sealed class StaticScopeFactory(IRunProvider runProvider, IRunCancellationService runCancellationService) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            return new StaticServiceScope(runProvider, runCancellationService);
        }
    }

    private sealed class StaticServiceScope(IRunProvider runProvider, IRunCancellationService runCancellationService) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider { get; } = new StaticServiceProvider(runProvider, runCancellationService);

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StaticServiceProvider(IRunProvider runProvider, IRunCancellationService runCancellationService) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IRunProvider))
            {
                return runProvider;
            }

            if (serviceType == typeof(IRunCancellationService))
            {
                return runCancellationService;
            }

            return null;
        }
    }
}

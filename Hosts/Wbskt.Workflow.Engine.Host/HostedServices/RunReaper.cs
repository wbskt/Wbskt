using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class RunReaper : BackgroundService
{
    private const int BatchSize = 100;
    private readonly IClock _clock;
    private readonly ILeadershipGate _leadership;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RunReaper> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _stuckThreshold;

    [ActivatorUtilitiesConstructor]
    public RunReaper(
        IClock clock,
        ILeadershipGate leadership,
        IServiceScopeFactory scopeFactory,
        ILogger<RunReaper> logger,
        IOptions<WorkflowEngineOptions> options)
        : this(clock, leadership, scopeFactory, logger, options.Value.RunReaperInterval, options.Value.RunStuckThreshold)
    {
    }

    internal RunReaper(
        IClock clock,
        ILeadershipGate leadership,
        IRunProvider runProvider,
        IRunCancellationService runCancellationService,
        IRunFinalizer runFinalizer,
        ILogger<RunReaper> logger,
        TimeSpan? pollInterval = null,
        TimeSpan? stuckThreshold = null)
        : this(clock, leadership, new StaticScopeFactory(runProvider, runCancellationService, runFinalizer), logger, pollInterval, stuckThreshold)
    {
    }

    private RunReaper(
        IClock clock,
        ILeadershipGate leadership,
        IServiceScopeFactory scopeFactory,
        ILogger<RunReaper> logger,
        TimeSpan? pollInterval,
        TimeSpan? stuckThreshold)
    {
        _clock = clock;
        _leadership = leadership;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _pollInterval = pollInterval ?? TimeSpan.FromMinutes(5);
        _stuckThreshold = stuckThreshold ?? TimeSpan.FromMinutes(30);
    }

    public async Task ProcessStuckRunsAsync(CancellationToken ct)
    {
        if (!_leadership.IsActive)
        {
            return;
        }

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        var runProvider = scope.ServiceProvider.GetRequiredService<IRunProvider>();
        var runCancellationService = scope.ServiceProvider.GetRequiredService<IRunCancellationService>();
        var runFinalizer = scope.ServiceProvider.GetRequiredService<IRunFinalizer>();
        DateTime cutoffUtc = _clock.UtcNow - _stuckThreshold;
        IReadOnlyCollection<RunRow> stuckRuns = await runProvider.GetStuckRunsAsync(cutoffUtc, BatchSize, ct);
        foreach (RunRow run in stuckRuns)
        {
            await runCancellationService.RequestCancellationAsync(run.Id, "REAPER_TIMEOUT", ct);
            // A stuck run (by Run_GetStuck's definition) has no bookmark and no live branch left to drive
            // finalization itself, so the reaper must finalize it directly. Safe because RunFinalizer is idempotent.
            await runFinalizer.FinalizeAsync(run.Id, ct);
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

    private sealed class StaticScopeFactory(IRunProvider runProvider, IRunCancellationService runCancellationService, IRunFinalizer runFinalizer) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            return new StaticServiceScope(runProvider, runCancellationService, runFinalizer);
        }
    }

    private sealed class StaticServiceScope(IRunProvider runProvider, IRunCancellationService runCancellationService, IRunFinalizer runFinalizer) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider { get; } = new StaticServiceProvider(runProvider, runCancellationService, runFinalizer);

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StaticServiceProvider(IRunProvider runProvider, IRunCancellationService runCancellationService, IRunFinalizer runFinalizer) : IServiceProvider
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

            if (serviceType == typeof(IRunFinalizer))
            {
                return runFinalizer;
            }

            return null;
        }
    }
}

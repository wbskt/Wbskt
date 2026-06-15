using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class RunRecoveryService : IHostedService, IEngineStartupTracker
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRunDispatcher _runDispatcher;
    private readonly ILogger<RunRecoveryService> _logger;
    private readonly TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Ready => _readyTcs.Task;

    public RunRecoveryService(
        IServiceScopeFactory scopeFactory,
        IRunDispatcher runDispatcher,
        ILogger<RunRecoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _runDispatcher = runDispatcher;
        _logger = logger;
    }

    internal RunRecoveryService(
        IBranchProvider branchProvider,
        IRunDispatcher runDispatcher,
        ILogger<RunRecoveryService> logger,
        IRunProvider? runProvider = null,
        IRunCancellationService? runCancellationService = null)
        : this(new StaticScopeFactory(branchProvider, runProvider, runCancellationService), runDispatcher, logger)
    {
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            var branchProvider = scope.ServiceProvider.GetRequiredService<IBranchProvider>();
            var runProvider = scope.ServiceProvider.GetService<IRunProvider>();
            var runCancellationService = scope.ServiceProvider.GetService<IRunCancellationService>();

            IReadOnlyCollection<BranchRow> branches = await branchProvider.GetRunningBranchesAsync(ct);
            
            var runGroups = branches.GroupBy(b => b.RunId);

            foreach (var group in runGroups)
            {
                long runId = group.Key;
                
                if (runProvider is not null && runCancellationService is not null)
                {
                    try
                    {
                        RunRow run = await runProvider.GetByIdAsync(runId, ct);
                        if (string.Equals(run.Status, "Cancelling", StringComparison.Ordinal) || string.Equals(run.Status, "Failing", StringComparison.Ordinal))
                        {
                            runCancellationService.CancelCts(runId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to inspect run {RunId} during startup recovery.", runId);
                    }
                }

                foreach (var branch in group)
                {
                    await _runDispatcher.DispatchAsync(new BranchExecutionRequest(branch.RunId, branch.Id, BranchExecutionReason.BookmarkResumed), ct);
                }
            }

            _logger.LogInformation("Recovered {Count} running branches.", branches.Count);
            _readyTcs.TrySetResult();
        }
        catch (Exception ex)
        {
            _readyTcs.TrySetException(ex);
            throw;
        }
    }

    public Task StopAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    private sealed class StaticScopeFactory(
        IBranchProvider branchProvider,
        IRunProvider? runProvider,
        IRunCancellationService? runCancellationService) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            return new StaticServiceScope(branchProvider, runProvider, runCancellationService);
        }
    }

    private sealed class StaticServiceScope(
        IBranchProvider branchProvider,
        IRunProvider? runProvider,
        IRunCancellationService? runCancellationService) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider { get; } = new StaticServiceProvider(branchProvider, runProvider, runCancellationService);

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StaticServiceProvider(
        IBranchProvider branchProvider,
        IRunProvider? runProvider,
        IRunCancellationService? runCancellationService) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IBranchProvider))
            {
                return branchProvider;
            }
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

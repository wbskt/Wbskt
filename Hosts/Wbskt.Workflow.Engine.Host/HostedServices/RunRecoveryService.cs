using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class RunRecoveryService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRunDispatcher _runDispatcher;
    private readonly ILogger<RunRecoveryService> _logger;

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
        ILogger<RunRecoveryService> logger)
        : this(new StaticScopeFactory(branchProvider), runDispatcher, logger)
    {
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        var branchProvider = scope.ServiceProvider.GetRequiredService<IBranchProvider>();
        IReadOnlyCollection<Wbskt.Workflow.Abstraction.Entities.BranchRow> branches = await branchProvider.GetRunningBranchesAsync(ct);
        foreach (Wbskt.Workflow.Abstraction.Entities.BranchRow branch in branches)
        {
            await _runDispatcher.DispatchAsync(new BranchExecutionRequest(branch.RunId, branch.Id, BranchExecutionReason.BookmarkResumed), ct);
        }

        _logger.LogInformation("Recovered {Count} running branches.", branches.Count);
    }

    public Task StopAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    private sealed class StaticScopeFactory(IBranchProvider branchProvider) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            return new StaticServiceScope(branchProvider);
        }
    }

    private sealed class StaticServiceScope(IBranchProvider branchProvider) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider { get; } = new StaticServiceProvider(branchProvider);

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StaticServiceProvider(IBranchProvider branchProvider) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(IBranchProvider) ? branchProvider : null;
        }
    }
}

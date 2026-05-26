using Microsoft.Extensions.Hosting;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class RunRecoveryService : IHostedService
{
    private readonly IBranchProvider _branchProvider;
    private readonly IRunDispatcher _runDispatcher;
    private readonly ILogger<RunRecoveryService> _logger;

    public RunRecoveryService(
        IBranchProvider branchProvider,
        IRunDispatcher runDispatcher,
        ILogger<RunRecoveryService> logger)
    {
        _branchProvider = branchProvider;
        _runDispatcher = runDispatcher;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        IReadOnlyCollection<Wbskt.Workflow.Abstraction.Entities.BranchRow> branches = await _branchProvider.GetRunningBranchesAsync(ct);
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
}

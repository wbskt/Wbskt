using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class RunReaper(
    TimeProvider clock,
    ILeadershipGate leadership,
    IServiceScopeFactory scopeFactory,
    ILogger<RunReaper> logger,
    IOptions<WorkflowEngineOptions> options) : LeaderPeriodicWorker(leadership, scopeFactory, logger, clock)
{
    private const int BatchSize = 100;
    private readonly TimeSpan _stuckThreshold = options.Value.RunStuckThreshold;

    protected override string Name => "Run reaper";

    protected override TimeSpan Interval { get; } = options.Value.RunReaperInterval;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var sweeper = services.GetRequiredService<SettledRunSweeper>();
        DateTime cutoffUtc = clock.GetUtcNow().UtcDateTime - _stuckThreshold;
        await sweeper.FinalizeAsync(cutoffUtc, BatchSize, ct);
    }
}

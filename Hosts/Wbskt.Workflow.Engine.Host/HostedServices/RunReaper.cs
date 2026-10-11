using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class RunReaper(
    IClock clock,
    ILeadershipGate leadership,
    IServiceScopeFactory scopeFactory,
    ILogger<RunReaper> logger,
    IOptions<WorkflowEngineOptions> options) : LeaderPeriodicWorker(leadership, scopeFactory, logger)
{
    private const int BatchSize = 100;
    private readonly TimeSpan _stuckThreshold = options.Value.RunStuckThreshold;

    protected override string Name => "Run reaper";

    protected override TimeSpan Interval { get; } = options.Value.RunReaperInterval;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var runProvider = services.GetRequiredService<IRunProvider>();
        var runCancellationService = services.GetRequiredService<IRunCancellationService>();
        var runFinalizer = services.GetRequiredService<IRunFinalizer>();
        DateTime cutoffUtc = clock.UtcNow - _stuckThreshold;
        IReadOnlyCollection<RunRow> stuckRuns = await runProvider.GetStuckRunsAsync(cutoffUtc, BatchSize, ct);
        foreach (RunRow run in stuckRuns)
        {
            await runCancellationService.RequestCancellationAsync(run.Id, "REAPER_TIMEOUT", ct);
            // A stuck run (by Run_GetStuck's definition) has no bookmark and no live branch left to drive
            // finalization itself, so the reaper must finalize it directly. Safe because RunFinalizer is idempotent.
            await runFinalizer.FinalizeAsync(run.Id, ct);
        }
    }
}

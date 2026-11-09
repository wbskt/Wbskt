using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

/// <summary>
/// Moves the credits runs consume into the ledger their plan's monthly allowance is counted against
/// (<c>dbo.WorkspaceCreditUsage</c>), off the hot path: a step's charge only touches its run's own
/// counter. Run_Start adds what the runs still going have not posted yet, so the lag here never lets a
/// start through on credits already spent. Runs on the active engine only.
/// </summary>
public sealed class CreditUsagePoster(
    TimeProvider clock,
    ILeadershipGate leadership,
    IServiceScopeFactory scopeFactory,
    ILogger<CreditUsagePoster> logger,
    IOptions<WorkflowEngineOptions> options) : LeaderPeriodicWorker(leadership, scopeFactory, logger)
{
    internal const int BatchSize = 500;
    private readonly TimeSpan _endedWindow = options.Value.CreditPostEndedWindow;

    protected override string Name => "Credit usage poster";

    protected override TimeSpan Interval { get; } = options.Value.CreditPostInterval;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var credits = services.GetRequiredService<ICreditUsageProvider>();
        DateTime nowUtc = clock.GetUtcNow().UtcDateTime;
        while (true)
        {
            int posted = await credits.PostAsync(DateOnly.FromDateTime(nowUtc), nowUtc - _endedWindow, BatchSize, ct);
            if (posted < BatchSize)
            {
                return;
            }
        }
    }
}

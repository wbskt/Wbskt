using Microsoft.Extensions.Options;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Billing;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

/// <summary>
/// Moves the credits runs consume into the ledger their plan's monthly allowance and their workspace's
/// cap are counted against (<c>dbo.WorkspaceCreditUsage</c>), off the hot path: a step's charge only
/// touches its run's own counter. Run_Start adds what the runs still going have not posted yet, so the
/// lag here never lets a start through on credits already spent. Warns when a post carries a workspace
/// to 80% or 100% of its cap (<see cref="WorkspaceCreditCapReachedEvent"/>). Runs on the active engine only.
/// </summary>
public sealed class CreditUsagePoster(
    TimeProvider clock,
    ILeadershipGate leadership,
    IServiceScopeFactory scopeFactory,
    ILogger<CreditUsagePoster> logger,
    IOptions<WorkflowEngineOptions> options) : LeaderPeriodicWorker(leadership, scopeFactory, logger)
{
    internal const int BatchSize = 500;

    // Runs that keep charging would keep a full batch posting forever; the next tick picks up the rest.
    internal const int MaxBatchesPerTick = 20;

    private readonly TimeSpan _endedWindow = options.Value.CreditPostEndedWindow;
    private readonly ILogger<CreditUsagePoster> _logger = logger;

    protected override string Name => "Credit usage poster";

    protected override TimeSpan Interval { get; } = options.Value.CreditPostInterval;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var credits = services.GetRequiredService<ICreditUsageProvider>();
        var eventBus = services.GetRequiredService<IEventBus>();
        DateTime nowUtc = clock.GetUtcNow().UtcDateTime;
        for (int batch = 0; batch < MaxBatchesPerTick; batch++)
        {
            IReadOnlyCollection<WorkspaceCreditPost> posted = await credits.PostAsync(DateOnly.FromDateTime(nowUtc), nowUtc - _endedWindow, BatchSize, ct);
            await WarnAsync(eventBus, posted, ct);

            if (posted.Sum(post => post.Runs) < BatchSize)
            {
                return;
            }
        }
    }

    // The credits are already posted, so a warning that fails to publish is logged and not retried.
    private async Task WarnAsync(IEventBus eventBus, IReadOnlyCollection<WorkspaceCreditPost> posted, CancellationToken ct)
    {
        foreach (WorkspaceCreditPost post in posted)
        {
            foreach (int percent in post.CapThresholdsCrossed())
            {
                try
                {
                    await eventBus.PublishAsync(new WorkspaceCreditCapReachedEvent(post.WorkspaceId, percent, post.MonthlyCreditCap!.Value, post.SpentThisMonth), ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Could not publish that workspace {WorkspaceId} reached {Percent}% of its credit cap", post.WorkspaceId, percent);
                }
            }
        }
    }
}

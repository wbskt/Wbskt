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
/// to 80% or 100% of its cap (<see cref="WorkspaceCreditCapReachedEvent"/>), and a tenant to 80% or 100% of its
/// plan's monthly credits (<see cref="TenantCreditAllowanceReachedEvent"/>). Runs on the active engine only.
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

    internal static readonly TimeSpan[] WarningRetryDelays = [TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

    private bool _brokerDown;

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

    // The crossing is worked out from this post's step, so it cannot be found again later: a warning is
    // tried a few times before it is given up and logged; once one is given up the broker is taken to be
    // down, and each is tried once until one goes through, so a long outage does not hold up the posting.
    // The auth host sends each notice once.
    private async Task WarnAsync(IEventBus eventBus, IReadOnlyCollection<WorkspaceCreditPost> posted, CancellationToken ct)
    {
        foreach (WorkspaceCreditPost post in posted)
        {
            foreach (int percent in post.CapThresholdsCrossed())
            {
                await PublishAsync(
                    eventBus,
                    new WorkspaceCreditCapReachedEvent(post.WorkspaceId, percent, post.MonthlyCreditCap!.Value, post.SpentThisMonth),
                    $"workspace {post.WorkspaceId} reached {percent}% of its credit cap",
                    ct);
            }
        }

        foreach (TenantAllowanceCrossing crossed in WorkspaceCreditPost.AllowanceThresholdsCrossed(posted))
        {
            await PublishAsync(
                eventBus,
                new TenantCreditAllowanceReachedEvent(crossed.WorkspaceId, crossed.TenantId, crossed.Percent, crossed.Allowance, crossed.Spent),
                $"tenant {crossed.TenantId} reached {crossed.Percent}% of its monthly credits",
                ct);
        }
    }

    private async Task PublishAsync<TEvent>(IEventBus eventBus, TEvent warning, string what, CancellationToken ct)
        where TEvent : IEvent
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                await eventBus.PublishAsync(warning, ct);
                _brokerDown = false;
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt == WarningRetryDelays.Length || _brokerDown)
                {
                    _brokerDown = true;
                    _logger.LogError(ex, "Could not publish that {What}; the warning is lost", what);
                    return;
                }

                _logger.LogWarning(ex, "Could not publish that {What}; trying again", what);
                await Task.Delay(WarningRetryDelays[attempt], clock, ct);
            }
        }
    }
}

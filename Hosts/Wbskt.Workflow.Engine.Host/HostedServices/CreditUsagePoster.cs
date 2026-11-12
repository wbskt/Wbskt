using Microsoft.Extensions.Options;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Billing;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

/// <summary>
/// Moves the credits runs consume into the ledger their plan's monthly allowance and their workspace's
/// cap are counted against (<c>dbo.WorkspaceCreditUsage</c>), off the hot path: a step's charge only
/// touches its run's own counter. Run_Start adds what runs have not posted yet, ended or not, so the lag here never
/// lets a start through on credits already spent. Then announces each workspace that reached 80% or 100%
/// of its cap (<see cref="WorkspaceCreditCapReachedEvent"/>) and each tenant that reached 80% or 100% of
/// its plan's monthly credits (<see cref="TenantCreditAllowanceReachedEvent"/>), from what the ledger holds
/// and what was already announced (<c>dbo.CreditNotices_GetDue</c>), marking each only once it is
/// published. Runs on the active engine only.
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

    // Notices are looked for after one that could not be published, at most once a minute after ticks
    // that posted, and at least every five minutes, which also finds those owed for credits
    // Run_DeleteRetired posted. The check reads every capped workspace and this month's tenants, so it
    // does not run on every ten-second tick.
    internal static readonly TimeSpan NoticeCheckAfterPost = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan NoticeCheckInterval = TimeSpan.FromMinutes(5);

    // A notice still refused after this many checks in a row is logged as an error rather than a warning.
    internal const int NoticeFailuresBeforeError = 10;

    private readonly ILogger<CreditUsagePoster> _logger = logger;

    private DateTime _lastNoticeCheck = DateTime.MinValue;
    private bool _noticesOwed;
    private int _noticeFailures;

    protected override string Name => "Credit usage poster";

    protected override TimeSpan Interval { get; } = options.Value.CreditPostInterval;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var credits = services.GetRequiredService<ICreditUsageProvider>();
        DateTime nowUtc = clock.GetUtcNow().UtcDateTime;
        bool posted = false;
        for (int batch = 0; batch < MaxBatchesPerTick; batch++)
        {
            IReadOnlyCollection<WorkspaceCreditPost> moved = await credits.PostAsync(DateOnly.FromDateTime(nowUtc), BatchSize, ct);
            posted |= moved.Any(post => post.Credits > 0);

            if (moved.Sum(post => post.Runs) < BatchSize)
            {
                break;
            }
        }

        TimeSpan sinceCheck = nowUtc - _lastNoticeCheck;
        if (_noticesOwed || sinceCheck >= NoticeCheckInterval || (posted && sinceCheck >= NoticeCheckAfterPost))
        {
            await AnnounceAsync(credits, services.GetRequiredService<IEventBus>(), nowUtc, ct);
        }
    }

    // A notice is marked only after it is published, so one the broker refuses stays owed and is tried
    // again on the next tick. The auth host sends each notice once a month, so a publish whose mark is
    // lost only repeats an event it ignores.
    private async Task AnnounceAsync(ICreditUsageProvider credits, IEventBus eventBus, DateTime nowUtc, CancellationToken ct)
    {
        var monthStart = new DateOnly(nowUtc.Year, nowUtc.Month, 1);
        _lastNoticeCheck = nowUtc;

        // Owed until a pass gets through every notice, so a failed read or mark is looked at again next tick.
        _noticesOwed = true;
        foreach (CreditNotice notice in await credits.GetDueNoticesAsync(monthStart, ct))
        {
            foreach (int percent in notice.Owed())
            {
                try
                {
                    await eventBus.PublishAsync(EventFor(notice, percent), ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _noticeFailures++;
                    _logger.Log(
                        _noticeFailures >= NoticeFailuresBeforeError ? LogLevel.Error : LogLevel.Warning,
                        ex,
                        "Could not publish the {Percent}% credit {Kind} notice for workspace {WorkspaceId} ({Failures} checks in a row); it is tried again on the next tick",
                        percent, notice.Kind, notice.WorkspaceId, _noticeFailures);
                    return;
                }

                await credits.MarkNotifiedAsync(notice, monthStart, percent, ct);
            }
        }

        _noticesOwed = false;
        _noticeFailures = 0;
    }

    private static IEvent EventFor(CreditNotice notice, int percent) => notice switch
    {
        { Kind: CreditNoticeKind.Cap } => new WorkspaceCreditCapReachedEvent(notice.WorkspaceId, percent, notice.Limit, notice.Spent),
        { Kind: CreditNoticeKind.Allowance, TenantId: { } tenantId } => new TenantCreditAllowanceReachedEvent(notice.WorkspaceId, tenantId, percent, notice.Limit, notice.Spent),
        _ => throw new InvalidOperationException($"A {notice.Kind} credit notice for workspace {notice.WorkspaceId} has no tenant.")
    };
}

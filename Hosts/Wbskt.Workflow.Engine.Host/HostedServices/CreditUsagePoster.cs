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
/// published. For the first <see cref="LateNoticeWindow"/> of a month it also announces the month before
/// reaching 100% when that was posted but not yet told, with that month on the event. Runs on the active
/// engine only.
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
    // Run_DeleteRetired posted. The check reads every capped workspace and the month's tenants, so it
    // does not run on every ten-second tick.
    internal static readonly TimeSpan NoticeCheckAfterPost = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan NoticeCheckInterval = TimeSpan.FromMinutes(5);

    // A crossing posted in a month's last minutes, or owed across a broker outage over midnight on the 1st,
    // is still announced for that month this long into the next (#876).
    internal static readonly TimeSpan LateNoticeWindow = TimeSpan.FromDays(7);

    // A notice still refused after this many checks in a row is logged as an error rather than a warning.
    internal const int NoticeFailuresBeforeError = 10;

    private readonly ILogger<CreditUsagePoster> _logger = logger;

    private DateTime _lastNoticeCheck = DateTime.MinValue;
    private bool _noticesOwed;
    private bool _postedSinceCheck;
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

        // A post too soon after the last check is remembered, so a quiet tick after it still checks.
        _postedSinceCheck |= posted;
        TimeSpan sinceCheck = nowUtc - _lastNoticeCheck;
        if (_noticesOwed || sinceCheck >= NoticeCheckInterval || (_postedSinceCheck && sinceCheck >= NoticeCheckAfterPost))
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
        _postedSinceCheck = false;

        // Owed until a pass gets through every notice, so a failed read or mark is looked at again next tick.
        _noticesOwed = true;

        // The month just ended goes first and must be settled before this one moves: a cap records the
        // one month it was last told about, and CreditNotices_GetDue counts a later month's mark as this one settled.
        bool lastMonthMayBeOwed = nowUtc < monthStart.ToDateTime(TimeOnly.MinValue) + LateNoticeWindow;
        if (lastMonthMayBeOwed && !await AnnounceMonthAsync(credits, eventBus, monthStart.AddMonths(-1), ended: true, ct))
        {
            return;
        }

        if (!await AnnounceMonthAsync(credits, eventBus, monthStart, ended: false, ct))
        {
            return;
        }

        _noticesOwed = false;
        _noticeFailures = 0;
    }

    private async Task<bool> AnnounceMonthAsync(ICreditUsageProvider credits, IEventBus eventBus, DateOnly monthStart, bool ended, CancellationToken ct)
    {
        foreach (CreditNotice notice in await credits.GetDueNoticesAsync(monthStart, ct))
        {
            IReadOnlyCollection<int> owed = ended ? notice.OwedAfterMonthEnd() : notice.Owed();
            foreach (int percent in owed)
            {
                try
                {
                    await eventBus.PublishAsync(EventFor(notice, percent, monthStart), ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _noticeFailures++;
                    _logger.Log(
                        _noticeFailures >= NoticeFailuresBeforeError ? LogLevel.Error : LogLevel.Warning,
                        ex,
                        "Could not publish the {Percent}% credit {Kind} notice for workspace {WorkspaceId} for {Month:yyyy-MM} ({Failures} checks in a row); it is tried again on the next tick",
                        percent, notice.Kind, notice.WorkspaceId, monthStart, _noticeFailures);
                    return false;
                }

                await credits.MarkNotifiedAsync(notice, monthStart, percent, ct);
            }

            // An ended month's 80% has nothing left to warn of, so it is settled without a notice.
            if (ended && owed.Count == 0)
            {
                await credits.MarkNotifiedAsync(notice, monthStart, notice.Level, ct);
            }
        }

        return true;
    }

    private static IEvent EventFor(CreditNotice notice, int percent, DateOnly monthStart) => notice switch
    {
        { Kind: CreditNoticeKind.Cap } => new WorkspaceCreditCapReachedEvent(notice.WorkspaceId, percent, notice.Limit, notice.Spent, monthStart),
        { Kind: CreditNoticeKind.Allowance, TenantId: { } tenantId } => new TenantCreditAllowanceReachedEvent(notice.WorkspaceId, tenantId, percent, notice.Limit, notice.Spent, monthStart),
        _ => throw new InvalidOperationException($"A {notice.Kind} credit notice for workspace {notice.WorkspaceId} has no tenant.")
    };
}

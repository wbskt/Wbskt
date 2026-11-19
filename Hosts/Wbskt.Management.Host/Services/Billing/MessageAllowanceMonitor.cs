using MassTransit;
using Microsoft.Extensions.Options;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Billing;
using Wbskt.Infrastructure.Billing;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Models.Billing;

namespace Wbskt.Management.Host.Services.Billing;

/// <summary>
/// Compares each account's client messages this month (the meter <c>dbo.ClientTraffic_InsertBatch</c>
/// keeps) with its plan's allowance. It tells every socket host which workspaces to throttle
/// (<see cref="MessageThrottleSnapshot"/>, the whole list every time), and announces each account once a
/// month and allowance, on the workspace that carried the most, as it passes 80% (<see cref="WorkspaceMessageLimitApproachingEvent"/>), 100%
/// (<see cref="WorkspaceMessageLimitReachedEvent"/>) and, on Maker, Pro and Team, the throttle at
/// its plan's <see cref="BillingOptions.PaidMessageThrottleMultiples"/> times it (<see cref="WorkspaceMessagesThrottledEvent"/>).
/// </summary>
public sealed class MessageAllowanceMonitor(
    IMessageAllowanceProvider allowance,
    PlanCatalog catalog,
    IPublishEndpoint publisher,
    IEventBus eventBus,
    TimeProvider clock,
    IOptions<BillingOptions> options,
    ILogger<MessageAllowanceMonitor> logger)
{
    private const int WarningPercent = 80;
    private const int ReachedPercent = 100;

    // The throttle notice's record: a key, not a share, since each plan is throttled at its own multiple.
    // 200 is what every paid plan used when the multiple was one number, so a month under way keeps its records.
    private const int ThrottledNotice = 200;

    private readonly BillingOptions _billing = options.Value;

    public async Task CheckAsync(CancellationToken ct)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        var monthStart = new DateOnly(now.Year, now.Month, 1);
        IReadOnlyCollection<WorkspaceMessageAccount> accounts = await allowance.GetAccountsAsync(monthStart, ct);

        List<MessageAllowanceUsage> usages = accounts
            .Select(account => (Account: account, Limits: catalog.LimitsFor(account.PlanCode, account.LimitOverrides)))
            .Where(u => u.Account.AccountMessages >= u.Limits.MessagesPerMonth * MessageAllowance.WarningShare)
            .Select(u => new MessageAllowanceUsage(
                u.Account,
                u.Limits.MessagesPerMonth,
                MessageAllowance.ThrottleAt(u.Account.PlanCode, u.Limits.MessagesPerMonth, _billing.PaidMessageThrottleMultipleFor(u.Account.PlanCode))))
            .ToList();

        List<MessageAllowanceUsage> throttled = usages.Where(u => u.IsThrottled).ToList();

        // Sent even when empty: that is how the socket hosts let go on the 1st or after an upgrade.
        await publisher.Publish(new MessageThrottleSnapshot(throttled.Select(u => u.Account.WorkspaceId).ToList(), now), ct);

        List<MessageAllowanceUsage> reached = usages.Where(u => u.Used >= u.Allowance).ToList();

        // The allowance is the account's, so each crossing is announced once per account, naming the
        // workspace that carried the most. Every workspace of it is recorded, so one listed later in the
        // month (a new workspace) does not raise the notice again. Every account past 80% is recorded, so
        // one found past 100% first is never warned later; only those still short of 100% are told, since
        // the next notice says more.
        foreach (MessageAllowanceUsage usage in await NewlyCrossedAsync(usages, monthStart, WarningPercent, ct))
        {
            if (usage.Used >= usage.Allowance)
            {
                continue;
            }

            logger.LogInformation("The account of workspace {WorkspaceId} has carried {Used} of its plan's {Limit} messages this month.",
                usage.Account.WorkspaceId, usage.Used, usage.Allowance);
            await AnnounceAsync(new WorkspaceMessageLimitApproachingEvent(usage.Account.WorkspaceId, usage.Allowance, usage.Used) { ThrottleAt = (long?)usage.ThrottleAt }, usages, usage, monthStart, WarningPercent, ct);
        }

        foreach (MessageAllowanceUsage usage in await NewlyCrossedAsync(reached, monthStart, ReachedPercent, ct))
        {
            logger.LogInformation("The account of workspace {WorkspaceId} reached its plan's {Limit} messages this month ({Used}); throttled: {Throttled}.",
                usage.Account.WorkspaceId, usage.Allowance, usage.Used, usage.IsThrottled);
            await AnnounceAsync(new WorkspaceMessageLimitReachedEvent(usage.Account.WorkspaceId, usage.Allowance, usage.Used, usage.IsThrottled) { ThrottleAt = (long?)usage.ThrottleAt }, reached, usage, monthStart, ReachedPercent, ct);
        }

        // A Free account's throttle starts at its allowance and is announced with it above.
        List<MessageAllowanceUsage> paidThrottled = throttled.Where(u => u.Account.PlanCode != PlanCode.Free).ToList();
        foreach (MessageAllowanceUsage usage in await NewlyCrossedAsync(paidThrottled, monthStart, ThrottledNotice, ct))
        {
            logger.LogWarning("The account of workspace {WorkspaceId} carried {Used} messages this month, {Multiple} times its plan's {Limit}; its clients are throttled until the 1st.",
                usage.Account.WorkspaceId, usage.Used, _billing.PaidMessageThrottleMultipleFor(usage.Account.PlanCode), usage.Allowance);
            await AnnounceAsync(new WorkspaceMessagesThrottledEvent(usage.Account.WorkspaceId, usage.Allowance, (long)usage.ThrottleAt!.Value, usage.Used), paidThrottled, usage, monthStart, ThrottledNotice, ct);
        }
    }

    // A workspace whose copy has not arrived is an account of its own.
    private static int AccountKey(MessageAllowanceUsage usage) => usage.Account.TenantId ?? -usage.Account.WorkspaceId;

    /// <summary>
    /// Records the notice for every workspace in <paramref name="usages"/> and returns one usage per account
    /// none of whose workspaces had it recorded before: the workspace that carried the most.
    /// </summary>
    private async Task<IReadOnlyCollection<MessageAllowanceUsage>> NewlyCrossedAsync(List<MessageAllowanceUsage> usages, DateOnly monthStart, int percent, CancellationToken ct)
    {
        IReadOnlyCollection<int> newly = await allowance.RecordNoticeAsync(usages.Select(u => new MessageLimitNotice(u.Account.WorkspaceId, u.Allowance)).ToList(), monthStart, percent, ct);
        return usages
            .GroupBy(AccountKey)
            .Where(account => account.All(u => newly.Contains(u.Account.WorkspaceId)))
            .Select(account => account.OrderByDescending(u => u.Account.WorkspaceMessages).ThenBy(u => u.Account.WorkspaceId).First())
            .ToList();
    }

    /// <summary>
    /// Publishes a notice already recorded. One that cannot be published is given back for every workspace
    /// of the account, so the next check raises it again rather than the account never hearing of it; the
    /// auth host sends each once.
    /// </summary>
    private async Task AnnounceAsync<TEvent>(TEvent notice, List<MessageAllowanceUsage> recorded, MessageAllowanceUsage named, DateOnly monthStart, int percent, CancellationToken ct)
        where TEvent : IWorkspaceContext
    {
        try
        {
            await eventBus.PublishAsync(notice, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not announce {Notice} for workspace {WorkspaceId}; it is raised again on the next check.", typeof(TEvent).Name, notice.WorkspaceId);
            foreach (MessageAllowanceUsage usage in recorded.Where(u => AccountKey(u) == AccountKey(named)))
            {
                await allowance.ReleaseNoticeAsync(new MessageLimitNotice(usage.Account.WorkspaceId, usage.Allowance), monthStart, percent, CancellationToken.None);
            }
        }
    }
}

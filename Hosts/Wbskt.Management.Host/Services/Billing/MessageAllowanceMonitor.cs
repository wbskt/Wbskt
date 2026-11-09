using MassTransit;
using Microsoft.Extensions.Options;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Billing;
using Wbskt.Infrastructure.Billing;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Models.Billing;

namespace Wbskt.Management.Host.Services.Billing;

/// <summary>
/// Compares each account's client messages this month (the meter <c>dbo.ClientTraffic_InsertBatch</c>
/// keeps) with its plan's allowance. It tells every socket host which workspaces to throttle
/// (<see cref="MessageThrottleSnapshot"/>, the whole list every time), and announces each workspace once a
/// month as it passes 80% (<see cref="WorkspaceMessageLimitApproachingEvent"/>), 100%
/// (<see cref="WorkspaceMessageLimitReachedEvent"/>) and, on Maker and Pro, the throttle at
/// <see cref="BillingOptions.PaidMessageThrottleMultiple"/> times it (<see cref="WorkspaceMessagesThrottledEvent"/>).
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

    private readonly decimal _paidThrottleMultiple = options.Value.PaidMessageThrottleMultiple;

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
                MessageAllowance.ThrottleAt(u.Account.PlanCode, u.Limits.MessagesPerMonth, _paidThrottleMultiple)))
            .ToList();

        List<MessageAllowanceUsage> throttled = usages.Where(u => u.IsThrottled).ToList();

        // Sent even when empty: that is how the socket hosts let go on the 1st or after an upgrade.
        await publisher.Publish(new MessageThrottleSnapshot(throttled.Select(u => u.Account.WorkspaceId).ToList(), now), ct);

        List<MessageAllowanceUsage> reached = usages.Where(u => u.Used >= u.Allowance).ToList();

        // Every account past 80% is recorded, so one found past 100% first is never warned later; only those
        // still short of 100% are told, since the next notice says more.
        IReadOnlyCollection<int> newlyWarned = await RecordAsync(usages, monthStart, WarningPercent, ct);
        foreach (MessageAllowanceUsage usage in usages.Where(u => newlyWarned.Contains(u.Account.WorkspaceId) && u.Used < u.Allowance))
        {
            logger.LogInformation("Workspace {WorkspaceId} has carried {Used} of its plan's {Limit} messages this month.",
                usage.Account.WorkspaceId, usage.Used, usage.Allowance);
            await eventBus.PublishAsync(new WorkspaceMessageLimitApproachingEvent(usage.Account.WorkspaceId, usage.Allowance, usage.Used), ct);
        }

        IReadOnlyCollection<int> newlyReached = await RecordAsync(reached, monthStart, ReachedPercent, ct);
        foreach (MessageAllowanceUsage usage in reached.Where(u => newlyReached.Contains(u.Account.WorkspaceId)))
        {
            logger.LogInformation("Workspace {WorkspaceId} reached its plan's {Limit} messages this month ({Used}); throttled: {Throttled}.",
                usage.Account.WorkspaceId, usage.Allowance, usage.Used, usage.IsThrottled);
            await eventBus.PublishAsync(new WorkspaceMessageLimitReachedEvent(usage.Account.WorkspaceId, usage.Allowance, usage.Used, usage.IsThrottled), ct);
        }

        // A Free account's throttle starts at its allowance and is announced with it above.
        List<MessageAllowanceUsage> paidThrottled = throttled.Where(u => u.Account.PlanCode != PlanCode.Free).ToList();
        IReadOnlyCollection<int> newlyThrottled = await RecordAsync(paidThrottled, monthStart, (int)(_paidThrottleMultiple * 100), ct);
        foreach (MessageAllowanceUsage usage in paidThrottled.Where(u => newlyThrottled.Contains(u.Account.WorkspaceId)))
        {
            logger.LogWarning("Workspace {WorkspaceId} carried {Used} messages this month, {Multiple} times its plan's {Limit}; its clients are throttled until the 1st.",
                usage.Account.WorkspaceId, usage.Used, _paidThrottleMultiple, usage.Allowance);
            await eventBus.PublishAsync(new WorkspaceMessagesThrottledEvent(usage.Account.WorkspaceId, usage.Allowance, (long)usage.ThrottleAt!.Value, usage.Used), ct);
        }
    }

    private Task<IReadOnlyCollection<int>> RecordAsync(List<MessageAllowanceUsage> usages, DateOnly monthStart, int percent, CancellationToken ct) =>
        allowance.RecordNoticeAsync(usages.Select(u => u.Account.WorkspaceId).ToList(), monthStart, percent, ct);
}

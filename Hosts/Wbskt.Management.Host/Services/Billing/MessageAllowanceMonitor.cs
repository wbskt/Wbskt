using MassTransit;
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
/// (<see cref="MessageThrottleSnapshot"/>, the whole list every time) and raises
/// <see cref="WorkspaceMessageLimitReachedEvent"/> once per workspace and month that is over.
/// </summary>
public sealed class MessageAllowanceMonitor(
    IMessageAllowanceProvider allowance,
    PlanCatalog catalog,
    IPublishEndpoint publisher,
    IEventBus eventBus,
    TimeProvider clock,
    ILogger<MessageAllowanceMonitor> logger)
{
    public async Task CheckAsync(CancellationToken ct)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        var monthStart = new DateOnly(now.Year, now.Month, 1);
        IReadOnlyCollection<WorkspaceMessageAccount> accounts = await allowance.GetAccountsAsync(monthStart, ct);

        var over = new List<(WorkspaceMessageAccount Account, PlanLimits Limits)>();
        foreach (WorkspaceMessageAccount account in accounts)
        {
            PlanLimits limits = catalog.LimitsFor(account.PlanCode, account.LimitOverrides);
            if (account.AccountMessages >= limits.MessagesPerMonth)
            {
                over.Add((account, limits));
            }
        }

        List<int> throttled = over
            .Where(o => MessageAllowance.IsThrottled(o.Account.PlanCode, o.Account.AccountMessages, o.Limits.MessagesPerMonth))
            .Select(o => o.Account.WorkspaceId)
            .ToList();

        // Sent even when empty: that is how the socket hosts let go on the 1st or after an upgrade.
        await publisher.Publish(new MessageThrottleSnapshot(throttled, now), ct);

        IReadOnlyCollection<int> newlyOver = await allowance.RecordLimitReachedAsync(over.Select(o => o.Account.WorkspaceId).ToList(), monthStart, ct);
        foreach ((WorkspaceMessageAccount account, PlanLimits limits) in over.Where(o => newlyOver.Contains(o.Account.WorkspaceId)))
        {
            bool isThrottled = MessageAllowance.IsThrottled(account.PlanCode, account.AccountMessages, limits.MessagesPerMonth);
            logger.LogInformation("Workspace {WorkspaceId} reached its plan's {Limit} messages this month ({Used}); throttled: {Throttled}.",
                account.WorkspaceId, limits.MessagesPerMonth, account.AccountMessages, isThrottled);
            await eventBus.PublishAsync(new WorkspaceMessageLimitReachedEvent(account.WorkspaceId, limits.MessagesPerMonth, account.AccountMessages, isThrottled), ct);
        }
    }
}

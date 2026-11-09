using MassTransit;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services.Billing;
using Wbskt.Auth.Models;
using Wbskt.Events.Auth;
using Wbskt.Events.Billing;
using Wbskt.Infrastructure.Billing;
using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Handlers;

/// <summary>
/// Turns what the engine and the management host report about a tenant's credits, messages and plan into
/// emails to its billing admins (<see cref="IBillingNotifier"/>), each once per period however often it is
/// reported: every workspace of an account reports its account's messages, and a redelivered event
/// reports again.
/// </summary>
internal sealed class BillingNoticeConsumer(
    IBillingNotifier notifier,
    ITenantPlanProvider plans,
    IWorkspaceProvider workspaces,
    PlanCatalog catalog,
    IConfiguration configuration)
    : IConsumer<TenantCreditAllowanceReachedEvent>,
        IConsumer<WorkspaceCreditCapReachedEvent>,
        IConsumer<WorkspaceMessageLimitApproachingEvent>,
        IConsumer<WorkspaceMessageLimitReachedEvent>,
        IConsumer<WorkspaceMessagesThrottledEvent>,
        IConsumer<TenantPlanChangedEvent>
{
    public Task Consume(ConsumeContext<TenantCreditAllowanceReachedEvent> context)
    {
        TenantCreditAllowanceReachedEvent e = context.Message;
        return notifier.NotifyAsync(e.TenantId, BillingNotices.CreditAllowance(e.Percent, e.CreditsPerMonth, e.CreditsSpent, e.CreatedAtUtc), context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<WorkspaceCreditCapReachedEvent> context)
    {
        WorkspaceCreditCapReachedEvent e = context.Message;
        if (await AccountOfAsync(e.WorkspaceId, context.CancellationToken) is not { } account)
        {
            return;
        }

        string name = (await workspaces.FindByIdAsync(e.WorkspaceId, context.CancellationToken))?.Name ?? "A workspace";
        await notifier.NotifyAsync(account.TenantId, BillingNotices.CreditCap(e.WorkspaceId, name, e.Percent, e.MonthlyCreditCap, e.CreditsSpent, e.CreatedAtUtc), context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<WorkspaceMessageLimitApproachingEvent> context)
    {
        WorkspaceMessageLimitApproachingEvent e = context.Message;
        if (await AccountOfAsync(e.WorkspaceId, context.CancellationToken) is { } account)
        {
            await notifier.NotifyAsync(account.TenantId, BillingNotices.MessagesApproaching(account.PlanCode, e.MessagesPerMonth, e.MessagesUsed, e.CreatedAtUtc), context.CancellationToken);
        }
    }

    public async Task Consume(ConsumeContext<WorkspaceMessageLimitReachedEvent> context)
    {
        WorkspaceMessageLimitReachedEvent e = context.Message;
        if (await AccountOfAsync(e.WorkspaceId, context.CancellationToken) is { } account)
        {
            await notifier.NotifyAsync(account.TenantId, BillingNotices.MessagesReached(account.PlanCode, e.MessagesPerMonth, e.MessagesUsed, e.Throttled, e.CreatedAtUtc), context.CancellationToken);
        }
    }

    public async Task Consume(ConsumeContext<WorkspaceMessagesThrottledEvent> context)
    {
        WorkspaceMessagesThrottledEvent e = context.Message;
        if (await AccountOfAsync(e.WorkspaceId, context.CancellationToken) is { } account)
        {
            await notifier.NotifyAsync(account.TenantId, BillingNotices.MessagesThrottled(account.PlanCode, e.MessagesPerMonth, e.ThrottledAt, e.MessagesUsed, e.CreatedAtUtc), context.CancellationToken);
        }
    }

    /// <summary>Only a move to a smaller plan is told: an upgrade is something the admin just did and sees.</summary>
    public Task Consume(ConsumeContext<TenantPlanChangedEvent> context)
    {
        TenantPlanChangedEvent e = context.Message;
        if (e.PlanCode >= e.PreviousPlanCode)
        {
            return Task.CompletedTask;
        }

        // The account's history is kept at the longer retention this long after the change (Docs/Billing.md, Downgrades).
        int retentionGraceDays = configuration.GetValue("Billing:RetentionGraceDays", 30);
        var notice = BillingNotices.Downgraded(
            e.PreviousPlanCode,
            catalog.LimitsFor(e.PreviousPlanCode),
            e.PlanCode,
            catalog.LimitsFor(e.PlanCode, e.LimitOverrides),
            e.PlanRevision,
            e.CreatedAtUtc.AddDays(retentionGraceDays));
        return notifier.NotifyAsync(e.TenantId, notice, context.CancellationToken);
    }

    // A workspace deleted since the event was raised has no account left to tell.
    private async Task<WorkspaceAccountResponse?> AccountOfAsync(int workspaceId, CancellationToken ct) =>
        (await plans.GetWorkspaceAccountsAsync(afterWorkspaceId: null, take: 1, tenantId: null, workspaceId: workspaceId, ct)).FirstOrDefault();
}

using Microsoft.Extensions.Options;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Models;
using Wbskt.Events.Billing;
using Wbskt.Infrastructure.Events.Outbox;
using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>
/// Brings a tenant's plan and credits into line with what the payment provider says now about a
/// subscription or a payment. Webhooks only say which one to read: their bodies can arrive late, twice
/// or out of order, and are never trusted for what was paid. Everything here is safe to repeat.
/// </summary>
internal sealed class TenantBillingSync(
    IPaymentGateway gateway,
    ITenantBillingProvider billing,
    ITenantPlanProvider plans,
    ITenantPlanService planService,
    IAuditWorkspaces audit,
    IOptions<BillingOptions> options,
    TimeProvider clock,
    ILogger<TenantBillingSync> logger)
{
    private const int PlanWriteAttempts = 3;

    /// <summary>
    /// Reads the subscription, stores it against the tenant its checkout was for, and moves the tenant
    /// to the plan it pays for. <paramref name="refundedThrough"/> marks a payment of it refunded.
    /// </summary>
    public async Task<BillingSyncResult> SyncSubscriptionAsync(string subscriptionId, DateTime? refundedThrough, CancellationToken ct)
    {
        ProviderSubscription? subscription = await gateway.FindSubscriptionAsync(subscriptionId, ct);
        if (subscription is null)
        {
            return BillingSyncResult.SetAside($"Subscription {subscriptionId} is not known to {gateway.Name}.");
        }

        if (subscription.TenantRef is not { } tenantRef || await plans.FindByRefIdAsync(tenantRef, ct) is not { } tenant)
        {
            return BillingSyncResult.SetAside($"Subscription {subscriptionId} names no tenant here.");
        }

        if (options.Value.FindPlan(subscription.ProductId) is not { } offer)
        {
            return BillingSyncResult.SetAside($"Subscription {subscriptionId} is for product {subscription.ProductId}, which is not a plan here.");
        }

        TenantSubscription? stored = await billing.FindSubscriptionAsync(tenant.TenantId, ct);
        if (stored is not null && stored.SubscriptionId != subscription.Id && SubscriptionPlan.IsLive(stored.Status) && !SubscriptionPlan.IsLive(subscription.Status))
        {
            // An older subscription ending after the tenant took out a new one: the new one decides.
            return BillingSyncResult.SetAside($"Subscription {subscriptionId} has ended and tenant {tenant.TenantId} has a newer one.");
        }

        DateTime now = clock.GetUtcNow().UtcDateTime;
        TenantSubscription saved = await billing.UpsertSubscriptionAsync(
            new TenantSubscription(
                tenant.TenantId,
                gateway.Name,
                subscription.Id,
                subscription.CustomerId,
                subscription.ProductId,
                offer.Plan,
                offer.Interval,
                subscription.Status,
                subscription.PeriodEnd.UtcDateTime,
                subscription.CancelAtPeriodEnd,
                PastDueSince: null,
                RefundedThrough: null),
            now,
            refundedThrough,
            ct);

        await ApplyAsync(tenant, saved, now, ct);
        return BillingSyncResult.Done;
    }

    /// <summary>
    /// Reads the payment: one for a subscription syncs that subscription (marking it refunded when the
    /// payment was); one for a credit pack grants its credits, or takes them back once refunded.
    /// </summary>
    public async Task<BillingSyncResult> SyncPaymentAsync(string paymentId, CancellationToken ct)
    {
        ProviderPayment? payment = await gateway.FindPaymentAsync(paymentId, ct);
        if (payment is null)
        {
            return BillingSyncResult.SetAside($"Payment {paymentId} is not known to {gateway.Name}.");
        }

        if (payment.SubscriptionId is { } subscriptionId)
        {
            DateTime? refundedThrough = null;
            if (payment.FullyRefunded && payment.Lines.Select(l => options.Value.FindPlan(l.ProductId)).FirstOrDefault(p => p is not null) is { } offer)
            {
                refundedThrough = SubscriptionPlan.RefundedThrough(payment.CreatedAt.UtcDateTime, offer.Interval);
            }

            return await SyncSubscriptionAsync(subscriptionId, refundedThrough, ct);
        }

        decimal credits = payment.Lines.Sum(l => (options.Value.FindCreditPack(l.ProductId)?.Credits ?? 0m) * l.Quantity);
        if (credits <= 0m)
        {
            return BillingSyncResult.SetAside($"Payment {paymentId} bought no credit pack offered here.");
        }

        return payment.FullyRefunded
            ? await RefundCreditPackAsync(payment, ct)
            : await GrantCreditPackAsync(payment, credits, ct);
    }

    /// <summary>
    /// Looks again at a subscription that may have lapsed with no webhook to say so. When the provider no
    /// longer knows it, the stored copy decides.
    /// </summary>
    public async Task ReviewAsync(int tenantId, CancellationToken ct)
    {
        TenantSubscription? stored = await billing.FindSubscriptionAsync(tenantId, ct);
        if (stored is null)
        {
            return;
        }

        BillingSyncResult result = await SyncSubscriptionAsync(stored.SubscriptionId, refundedThrough: null, ct);
        if (result.Reason is not null && await plans.FindByIdAsync(tenantId, ct) is { } tenant)
        {
            logger.LogWarning("Reviewing tenant {TenantId}'s subscription from its stored copy: {Reason}", tenantId, result.Reason);
            await ApplyAsync(tenant, stored, clock.GetUtcNow().UtcDateTime, ct);
        }
    }

    private async Task<BillingSyncResult> GrantCreditPackAsync(ProviderPayment payment, decimal credits, CancellationToken ct)
    {
        if (!payment.Succeeded)
        {
            return BillingSyncResult.SetAside($"Payment {payment.Id} has not succeeded.");
        }

        if (payment.TenantRef is not { } tenantRef || await plans.FindByRefIdAsync(tenantRef, ct) is not { } tenant)
        {
            return BillingSyncResult.SetAside($"Payment {payment.Id} names no tenant here.");
        }

        DateTime purchasedAt = payment.CreatedAt.UtcDateTime;
        DateTime expiresAt = purchasedAt + options.Value.CreditPackValidity;
        var granted = new TenantCreditsGrantedEvent(tenant.TenantId, tenant.TenantRefId, credits, new DateTimeOffset(expiresAt, TimeSpan.Zero), gateway.Name, payment.Id)
        {
            WorkspaceIds = await audit.OfTenantAsync(tenant.TenantId, ct)
        };

        if (await billing.RecordCreditPurchaseAsync(gateway.Name, payment.Id, tenant.TenantId, payment.CustomerId, credits, purchasedAt, expiresAt, OutboxMessage.Create(granted), ct))
        {
            logger.LogInformation("Tenant {TenantId} bought {Credits} credits with payment {PaymentId}, spendable until {ExpiresAt:o}.", tenant.TenantId, credits, payment.Id, expiresAt);
        }

        return BillingSyncResult.Done;
    }

    private async Task<BillingSyncResult> RefundCreditPackAsync(ProviderPayment payment, CancellationToken ct)
    {
        TenantCreditPurchase? purchase = await billing.FindCreditPurchaseAsync(gateway.Name, payment.Id, ct);
        if (purchase is null)
        {
            // Refunded before its purchase was recorded: there is nothing to take back, and nothing will be granted.
            return BillingSyncResult.SetAside($"Payment {payment.Id} was refunded before its credits were granted.");
        }

        if (purchase.RefundedAt is not null || await plans.FindByIdAsync(purchase.TenantId, ct) is not { } tenant)
        {
            return BillingSyncResult.Done;
        }

        var revoked = new TenantCreditGrantRevokedEvent(tenant.TenantId, tenant.TenantRefId, gateway.Name, payment.Id)
        {
            WorkspaceIds = await audit.OfTenantAsync(tenant.TenantId, ct)
        };

        if (await billing.RefundCreditPurchaseAsync(gateway.Name, payment.Id, clock.GetUtcNow().UtcDateTime, OutboxMessage.Create(revoked), ct))
        {
            logger.LogInformation("Took back the {Credits} credits tenant {TenantId} bought with refunded payment {PaymentId}.", purchase.Credits, tenant.TenantId, payment.Id);
        }

        return BillingSyncResult.Done;
    }

    /// <summary>
    /// Puts the tenant on the plan its subscription decides, keeping its overrides. A plan changed under
    /// it in the meantime is read again and the decision written over it.
    /// </summary>
    private async Task ApplyAsync(TenantPlan tenant, TenantSubscription subscription, DateTime now, CancellationToken ct)
    {
        if (SubscriptionPlan.Decide(subscription, now, options.Value.PastDueGrace) is not { } target)
        {
            return;
        }

        TenantPlan current = tenant;
        for (int attempt = 1; current.PlanCode != target; attempt++)
        {
            var request = new SetTenantPlanRequest(target, current.LimitOverrides, current.PlanRevision);
            var result = await planService.SetAsync(current.TenantRefId, request, ct);
            if (result.IsSuccess)
            {
                logger.LogInformation("Tenant {TenantId} is now on {Plan}: its subscription {SubscriptionId} is {Status}.", current.TenantId, target, subscription.SubscriptionId, subscription.Status);
                return;
            }

            if (attempt == PlanWriteAttempts || await plans.FindByIdAsync(current.TenantId, ct) is not { } reread)
            {
                throw new InvalidOperationException($"Could not move tenant {current.TenantId} to {target}: {result.Error.Code}.");
            }

            current = reread;
        }
    }
}

/// <summary>How a sync ended: done, or set aside with the reason when the provider's answer gives nothing to act on.</summary>
public sealed record BillingSyncResult(string? Reason)
{
    public static readonly BillingSyncResult Done = new((string?)null);

    public static BillingSyncResult SetAside(string reason) => new(reason);
}

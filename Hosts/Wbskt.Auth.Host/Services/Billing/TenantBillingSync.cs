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
    IBillingNotifier notifier,
    IOptions<BillingOptions> options,
    TimeProvider clock,
    ILogger<TenantBillingSync> logger)
{
    private const int PlanWriteAttempts = 3;

    /// <summary>
    /// Reads the subscription, stores it against the tenant its checkout was for, cancels any subscription
    /// the tenant is paying for twice, and moves the tenant to the plan its deciding subscription pays for.
    /// </summary>
    public async Task<BillingSyncResult> SyncSubscriptionAsync(string subscriptionId, CancellationToken ct) =>
        (await SyncStoredSubscriptionAsync(subscriptionId, reversed: null, ct)).Result;

    /// <summary>
    /// Reads the payment: one for a subscription syncs that subscription (marking it refunded when the
    /// payment was refunded or lost to a chargeback, and refunding it when the subscription was a
    /// duplicate); one for a credit pack grants its credits, holds them while a chargeback is open, or
    /// takes them back once the money went back.
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
            // The period paid for comes from the plan the payment's lines name, and from the subscription's
            // own product when it carries none: a subscription payment may have no product cart, and a
            // refund must drop the plan all the same.
            ReversedPayment? reversed = payment.Reversed
                ? new ReversedPayment(payment.CreatedAt.UtcDateTime, payment.Lines.Select(l => options.Value.FindPlan(l.ProductId)).FirstOrDefault(o => o is not null)?.Interval)
                : null;
            (BillingSyncResult result, TenantSubscription? stored) = await SyncStoredSubscriptionAsync(subscriptionId, reversed, ct);
            if (stored is { DuplicateAt: not null, DuplicateRefundPaymentId: null } && payment is { Succeeded: true, Reversed: false })
            {
                await RefundDuplicateAsync(stored, payment, ct);
            }

            return result;
        }

        decimal credits = payment.Lines.Sum(l => (options.Value.FindCreditPack(l.ProductId)?.Credits ?? 0m) * l.Quantity);
        if (credits <= 0m)
        {
            return BillingSyncResult.SetAside($"Payment {paymentId} bought no credit pack offered here.");
        }

        if (payment.Reversed)
        {
            return await RefundCreditPackAsync(payment, ct);
        }

        BillingSyncResult granted = await GrantCreditPackAsync(payment, credits, ct);
        return granted.Reason is null ? await HoldCreditPackAsync(payment, ct) : granted;
    }

    /// <summary>
    /// Looks again at a tenant whose plan may have to change with no webhook to say so: a subscription
    /// that may have lapsed, or an operator's plan past its end. When the provider no longer knows the
    /// subscription, the stored copy decides.
    /// </summary>
    public async Task ReviewAsync(int tenantId, CancellationToken ct)
    {
        if (await plans.FindByIdAsync(tenantId, ct) is not { } tenant)
        {
            return;
        }

        TenantSubscription? deciding = SubscriptionPlan.Deciding(await billing.GetSubscriptionsAsync(tenantId, ct));
        if (deciding is null || !gateway.IsConfigured)
        {
            await ApplyAsync(tenant, deciding, clock.GetUtcNow().UtcDateTime, ct);
            return;
        }

        BillingSyncResult result = await SyncSubscriptionAsync(deciding.SubscriptionId, ct);
        if (result.Reason is not null && await plans.FindByIdAsync(tenantId, ct) is { } reread)
        {
            logger.LogWarning("Reviewing tenant {TenantId}'s subscription from its stored copy: {Reason}", tenantId, result.Reason);
            await ApplyAsync(reread, deciding, clock.GetUtcNow().UtcDateTime, ct);
        }
    }

    /// <summary>
    /// Reads the subscription and stores it. <paramref name="reversed"/>, when set, is a payment of it that
    /// was refunded or lost to a chargeback, which marks the period it paid for refunded.
    /// </summary>
    private async Task<(BillingSyncResult Result, TenantSubscription? Stored)> SyncStoredSubscriptionAsync(string subscriptionId, ReversedPayment? reversed, CancellationToken ct)
    {
        ProviderSubscription? subscription = await gateway.FindSubscriptionAsync(subscriptionId, ct);
        if (subscription is null)
        {
            return (BillingSyncResult.SetAside($"Subscription {subscriptionId} is not known to {gateway.Name}."), null);
        }

        if (subscription.TenantRef is not { } tenantRef || await plans.FindByRefIdAsync(tenantRef, ct) is not { } tenant)
        {
            return (BillingSyncResult.SetAside($"Subscription {subscriptionId} names no tenant here."), null);
        }

        if (options.Value.FindPlan(subscription.ProductId) is not { } offer)
        {
            return (BillingSyncResult.SetAside($"Subscription {subscriptionId} is for product {subscription.ProductId}, which is not a plan here."), null);
        }

        // A change scheduled to a product that is not a plan here is not shown; the sync after it applies
        // sets it aside as it would any such subscription.
        var scheduled = subscription.Scheduled is { } change ? options.Value.FindPlan(change.ProductId) : null;
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
                RefundedThrough: null)
            {
                ScheduledPlan = scheduled?.Plan,
                ScheduledInterval = scheduled?.Interval,
                ScheduledAt = scheduled is null ? null : subscription.Scheduled!.EffectiveAt.UtcDateTime
            },
            now,
            reversed is { } payment ? SubscriptionPlan.RefundedThrough(payment.PaidAt, payment.Interval ?? offer.Interval) : null,
            ct);

        await TellPaymentFailedAsync(saved, ct);

        IReadOnlyCollection<TenantSubscription> all = await CancelDuplicatesAsync(tenant, await billing.GetSubscriptionsAsync(tenant.TenantId, ct), now, ct);
        await ApplyAsync(tenant, SubscriptionPlan.Deciding(all), now, ct);
        return (BillingSyncResult.Done, all.FirstOrDefault(s => s.SubscriptionId == saved.SubscriptionId) ?? saved);
    }

    /// <summary>
    /// Tells the billing admins once that a renewal failed and until when the plan holds. The subscription
    /// is already stored, so a notice that cannot be sent is logged rather than failing the sync.
    /// </summary>
    private async Task TellPaymentFailedAsync(TenantSubscription saved, CancellationToken ct)
    {
        if (saved is not { PastDueSince: { } pastDueSince, DuplicateAt: null })
        {
            return;
        }

        try
        {
            await notifier.NotifyAsync(saved.TenantId, BillingNotices.PaymentFailed(saved.SubscriptionId, saved.PlanCode, pastDueSince, options.Value.PastDueGrace), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not tell tenant {TenantId} that subscription {SubscriptionId}'s payment failed.", saved.TenantId, saved.SubscriptionId);
        }
    }

    /// <summary>
    /// Cancels each subscription billing beside the tenant's deciding one, as two checkouts paid at once
    /// leave, and any marked before whose cancel did not go through. Returns the subscriptions with the
    /// marks. A cancel the provider refuses throws, so the webhook is delivered again.
    /// </summary>
    private async Task<IReadOnlyCollection<TenantSubscription>> CancelDuplicatesAsync(TenantPlan tenant, IReadOnlyCollection<TenantSubscription> subscriptions, DateTime now, CancellationToken ct)
    {
        var duplicates = SubscriptionPlan.Duplicates(subscriptions).Select(s => s.SubscriptionId).ToHashSet(StringComparer.Ordinal);
        var marked = subscriptions.Select(s => duplicates.Contains(s.SubscriptionId) ? s with { DuplicateAt = now } : s).ToList();
        foreach (TenantSubscription duplicate in marked.Where(s => s.DuplicateAt is not null && SubscriptionPlan.IsBilling(s)))
        {
            if (duplicates.Contains(duplicate.SubscriptionId))
            {
                await billing.MarkDuplicateSubscriptionAsync(duplicate.Provider, duplicate.SubscriptionId, now, ct);
                logger.LogError(
                    "Tenant {TenantId} paid for subscription {SubscriptionId} ({Plan}) beside another it already has; cancelling it and refunding its payment.",
                    tenant.TenantId, duplicate.SubscriptionId, duplicate.PlanCode);
            }

            await gateway.CancelSubscriptionAsync(duplicate.SubscriptionId, ct);
        }

        return marked;
    }

    /// <summary>
    /// Refunds the payment of a subscription cancelled as a duplicate, once. The claim is taken first, so a
    /// redelivered webhook does not ask again. It is given back only when the provider refused the request (a
    /// 4xx), since then nothing was refunded. A timeout, a dropped connection, a server error or a cancelled
    /// request may have
    /// reached the provider, and asking again could refund twice, so the claim stays and an operator checks
    /// the payment (the error names it).
    /// </summary>
    private async Task RefundDuplicateAsync(TenantSubscription duplicate, ProviderPayment payment, CancellationToken ct)
    {
        if (!await billing.ClaimDuplicateRefundAsync(duplicate.Provider, duplicate.SubscriptionId, payment.Id, release: false, clock.GetUtcNow().UtcDateTime, ct))
        {
            return;
        }

        try
        {
            await gateway.RefundPaymentAsync(payment.Id, "Duplicate subscription: the account already had one.", ct);
            logger.LogWarning("Refunded payment {PaymentId} of duplicate subscription {SubscriptionId} of tenant {TenantId}.", payment.Id, duplicate.SubscriptionId, duplicate.TenantId);
        }
        catch (HttpRequestException refused) when (refused.StatusCode is { } status && (int)status is >= 400 and < 500)
        {
            await ReleaseRefundClaimAsync(duplicate, payment, refused);
            throw;
        }
        catch (Exception unanswered)
        {
            logger.LogError(unanswered,
                "The refund of payment {PaymentId} of duplicate subscription {SubscriptionId} of tenant {TenantId} got no answer. It may have gone through, so it is not asked for again: check the payment with the provider and refund it by hand if it was not.",
                payment.Id, duplicate.SubscriptionId, duplicate.TenantId);
            throw;
        }
    }

    /// <summary>
    /// Gives back a refund claim after the provider refused the refund. A failure to give it back is logged,
    /// so the refusal, not the release, is what the caller sees.
    /// </summary>
    private async Task ReleaseRefundClaimAsync(TenantSubscription duplicate, ProviderPayment payment, HttpRequestException refused)
    {
        try
        {
            await billing.ClaimDuplicateRefundAsync(duplicate.Provider, duplicate.SubscriptionId, payment.Id, release: true, clock.GetUtcNow().UtcDateTime, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not give back the refund claim on payment {PaymentId} after the provider refused it ({Status}).", payment.Id, refused.StatusCode);
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
    /// Holds a credit pack's grant while a chargeback on its payment is open, and releases it once the
    /// chargeback is won or withdrawn. Each change is made from the revision read, so a stale one does
    /// nothing; a refunded pack is never released.
    /// </summary>
    private async Task<BillingSyncResult> HoldCreditPackAsync(ProviderPayment payment, CancellationToken ct)
    {
        bool held = payment.Dispute == PaymentDispute.Open;
        TenantCreditPurchase? purchase = await billing.FindCreditPurchaseAsync(gateway.Name, payment.Id, ct);
        if (purchase is null || purchase.RefundedAt is not null || (purchase.HeldAt is not null) == held || await plans.FindByIdAsync(purchase.TenantId, ct) is not { } tenant)
        {
            return BillingSyncResult.Done;
        }

        var changed = new TenantCreditGrantHoldChangedEvent(tenant.TenantId, tenant.TenantRefId, gateway.Name, payment.Id, held, purchase.HoldRevision + 1)
        {
            WorkspaceIds = await audit.OfTenantAsync(tenant.TenantId, ct)
        };

        if (await billing.SetCreditPurchaseHoldAsync(gateway.Name, payment.Id, held, clock.GetUtcNow().UtcDateTime, purchase.HoldRevision, OutboxMessage.Create(changed), ct))
        {
            logger.LogWarning(held
                    ? "Holding the {Credits} credits tenant {TenantId} bought with payment {PaymentId}: a chargeback on it is open."
                    : "Released the {Credits} credits tenant {TenantId} bought with payment {PaymentId}: its chargeback was won or withdrawn.",
                purchase.Credits, tenant.TenantId, payment.Id);
        }

        return BillingSyncResult.Done;
    }

    /// <summary>
    /// Puts the tenant on the plan its deciding subscription pays for, or on Free when it has none, unless
    /// an operator's plan still holds. Overrides are kept, except an ended operator plan's, which end with
    /// it. A plan changed under it in the meantime is read again and the decision written over it.
    /// </summary>
    private async Task ApplyAsync(TenantPlan tenant, TenantSubscription? subscription, DateTime now, CancellationToken ct)
    {
        PlanCode? decided = subscription is null ? PlanCode.Free : SubscriptionPlan.Decide(subscription, now, options.Value.PastDueGrace);
        if (decided is not { } target)
        {
            return;
        }

        PlanSource source = subscription is null ? PlanSource.Default : PlanSource.Subscription;
        TenantPlan current = tenant;
        for (int attempt = 1; current.PlanCode != target || current.Source == PlanSource.Operator; attempt++)
        {
            if (current.IsOperatorPlanAt(now))
            {
                logger.LogInformation(
                    "Left tenant {TenantId} on {Plan}, which an operator set until {EndsAt}, though its subscription says {Target}.",
                    current.TenantId, current.PlanCode, current.EndsAt?.ToString("o") ?? "further notice", target);
                return;
            }

            PlanLimitOverrides? overrides = current.Source == PlanSource.Operator ? null : current.LimitOverrides;
            var request = new SetTenantPlanRequest(target, overrides, current.PlanRevision);
            var result = await planService.SetAsync(current.TenantRefId, request, source, ct);
            if (result.IsSuccess)
            {
                logger.LogInformation(
                    "Tenant {TenantId} is now on {Plan}: its subscription {SubscriptionId} is {Status}.",
                    current.TenantId, target, subscription?.SubscriptionId ?? "(none)", subscription?.Status.ToString() ?? "absent");
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

/// <summary>
/// A subscription payment refunded or lost to a chargeback: when it was paid, and the interval its lines
/// name, when they name a plan.
/// </summary>
internal sealed record ReversedPayment(DateTime PaidAt, BillingInterval? Interval);

using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Models;

/// <summary>
/// A tenant's subscription as <c>dbo.TenantSubscriptions</c> holds it: as last read from the provider.
/// <c>CurrentPeriodEnd</c> is the end of what has been paid for; <c>PastDueSince</c> is when a renewal
/// first failed (null while it is paid up); <c>RefundedThrough</c> is the end of a refunded payment's
/// period, before which the paid plan is withheld.
/// </summary>
public sealed record TenantSubscription(
    int TenantId,
    string Provider,
    string SubscriptionId,
    string CustomerId,
    string ProductId,
    PlanCode PlanCode,
    BillingInterval Interval,
    SubscriptionStatus Status,
    DateTime CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    DateTime? PastDueSince,
    DateTime? RefundedThrough);

/// <summary>A webhook delivery as stored: its row, and when an earlier delivery of it was acted on.</summary>
public sealed record BillingWebhookDelivery(long Id, DateTime? ProcessedAt);

/// <summary>A credit pack bought: the tenant it is for, and when it was refunded.</summary>
public sealed record TenantCreditPurchase(int TenantId, decimal Credits, DateTime? RefundedAt);

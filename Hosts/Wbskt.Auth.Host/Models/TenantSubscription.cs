using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Models;

/// <summary>
/// A tenant's subscription as <c>dbo.TenantSubscriptions</c> holds it: as last read from the provider.
/// <c>CurrentPeriodEnd</c> is the end of what has been paid for; <c>PastDueSince</c> is when a renewal
/// first failed (null while it is paid up); <c>RefundedThrough</c> is the end of a refunded or charged-back
/// payment's period, before which the paid plan is withheld. <c>DuplicateAt</c> is when it was found
/// billing beside an older subscription of the tenant's and cancelled; <c>DuplicateRefundPaymentId</c> is
/// its payment refunded for that. <c>CreatedAt</c> is when it was first stored here.
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
    DateTime? RefundedThrough,
    DateTime? DuplicateAt = null,
    string? DuplicateRefundPaymentId = null,
    DateTime CreatedAt = default);

/// <summary>A webhook delivery as stored: its row, and when an earlier delivery of it was acted on.</summary>
public sealed record BillingWebhookDelivery(long Id, DateTime? ProcessedAt);

/// <summary>
/// A credit pack bought: the tenant it is for, when it was refunded, and its hold while a chargeback on
/// it is open (<c>HoldRevision</c> goes up with every hold and release).
/// </summary>
public sealed record TenantCreditPurchase(int TenantId, decimal Credits, DateTime? RefundedAt, DateTime? HeldAt = null, int HoldRevision = 0);

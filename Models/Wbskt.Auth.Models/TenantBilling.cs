using Wbskt.Models.Billing;

namespace Wbskt.Auth.Models;

/// <summary>
/// What to buy: a paid plan billed every <c>Interval</c>, or a credit pack by name. Exactly one of
/// <c>Plan</c> and <c>CreditPack</c>.
/// </summary>
public sealed record BillingCheckoutRequest(PlanCode? Plan = null, BillingInterval? Interval = null, string? CreditPack = null);

/// <summary>The payment provider's hosted checkout page. Payment happens there; the plan changes when the provider confirms it.</summary>
public sealed record BillingCheckoutResponse(string CheckoutUrl);

/// <summary>The payment provider's customer portal, for card changes, invoices and cancelling.</summary>
public sealed record BillingPortalResponse(string Url);

/// <summary>
/// The tenant's plan, its subscription if it has one, and what can be bought: the paid plans and
/// intervals and the credit packs this deployment offers.
/// </summary>
public sealed record TenantBillingResponse(
    PlanCode Plan,
    TenantSubscriptionResponse? Subscription,
    IReadOnlyCollection<PlanOffer> Plans,
    IReadOnlyCollection<CreditPackOffer> CreditPacks);

/// <param name="PeriodEnd">The end of the period paid for: when it renews, or when a cancelled subscription ends.</param>
/// <param name="CancelAtPeriodEnd">True when it has been cancelled and stops at <c>PeriodEnd</c>.</param>
/// <param name="PastDueSince">When a renewal first failed; the plan drops to Free after the grace period.</param>
public sealed record TenantSubscriptionResponse(
    PlanCode Plan,
    BillingInterval Interval,
    SubscriptionStatus Status,
    DateTimeOffset PeriodEnd,
    bool CancelAtPeriodEnd,
    DateTimeOffset? PastDueSince);

public sealed record PlanOffer(PlanCode Plan, BillingInterval Interval);

public sealed record CreditPackOffer(string Name, decimal Credits);

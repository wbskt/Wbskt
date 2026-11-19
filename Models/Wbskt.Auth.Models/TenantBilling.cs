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
/// intervals and the credit packs this deployment offers. No plan is offered while Wbskt has set the
/// plan (<c>Source</c> Operator, until <c>EndsAt</c>): a subscription would charge beside it and change nothing.
/// </summary>
/// <param name="Source">Who set the plan: Default, the tenant's Subscription, or Operator (a Business contract or a plan Wbskt set).</param>
/// <param name="EndsAt">When a plan Wbskt set ends; null when it has no end, or for any other source.</param>
/// <param name="Limits">What the plan allows the tenant, with its own overrides applied.</param>
public sealed record TenantBillingResponse(
    PlanCode Plan,
    TenantSubscriptionResponse? Subscription,
    IReadOnlyCollection<PlanOffer> Plans,
    IReadOnlyCollection<CreditPackOffer> CreditPacks,
    PlanSource Source,
    DateTimeOffset? EndsAt,
    PlanLimits Limits);

/// <param name="PeriodEnd">The end of the period paid for: when it renews, or when a cancelled subscription ends.</param>
/// <param name="CancelAtPeriodEnd">True when it has been cancelled and stops at <c>PeriodEnd</c>.</param>
/// <param name="PastDueSince">When a renewal first failed; the plan drops to Free after the grace period.</param>
/// <param name="ScheduledPlan">A downgrade asked for, which takes effect at <c>ScheduledAt</c>; null for none.</param>
/// <param name="ScheduledInterval">The interval the downgrade bills at.</param>
/// <param name="ScheduledAt">When the downgrade takes effect: the end of the period paid for.</param>
public sealed record TenantSubscriptionResponse(
    PlanCode Plan,
    BillingInterval Interval,
    SubscriptionStatus Status,
    DateTimeOffset PeriodEnd,
    bool CancelAtPeriodEnd,
    DateTimeOffset? PastDueSince,
    PlanCode? ScheduledPlan,
    BillingInterval? ScheduledInterval,
    DateTimeOffset? ScheduledAt);

/// <summary>
/// The plan to move a subscription to. A higher plan, or the same plan billed yearly instead of monthly,
/// takes effect at once and the difference for the rest of the period is charged now; anything else
/// takes effect at the end of the period paid for. Asking for the plan already held drops a scheduled
/// change.
/// </summary>
public sealed record ChangePlanRequest(PlanCode Plan, BillingInterval Interval);

/// <summary>A plan on sale, with what it costs for the interval in USD and what it allows.</summary>
public sealed record PlanOffer(PlanCode Plan, BillingInterval Interval, decimal AmountUsd, PlanLimits Limits);

/// <summary>A credit pack on sale, with what it costs in USD.</summary>
public sealed record CreditPackOffer(string Name, decimal Credits, decimal AmountUsd);

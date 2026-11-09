using Wbskt.Auth.Host.Models;
using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>
/// What plan a tenant's subscription puts it on now. A subscription paid up, or past due within the grace
/// period, or cancelled but not yet at the end of the period paid for, keeps its paid plan; one that has
/// failed, expired or been paused, or whose payment was refunded, puts the tenant on Free. A pending one
/// (checkout started, not paid) changes nothing.
/// </summary>
public static class SubscriptionPlan
{
    public static PlanCode? Decide(TenantSubscription subscription, DateTime nowUtc, TimeSpan pastDueGrace)
    {
        if (subscription.RefundedThrough is { } refunded && subscription.CurrentPeriodEnd <= refunded)
        {
            return PlanCode.Free;
        }

        return subscription.Status switch
        {
            SubscriptionStatus.Pending => null,
            SubscriptionStatus.Active => subscription.PlanCode,
            SubscriptionStatus.PastDue or SubscriptionStatus.OnHold =>
                subscription.PastDueSince is { } since && nowUtc - since >= pastDueGrace ? PlanCode.Free : subscription.PlanCode,
            SubscriptionStatus.Cancelled => nowUtc < subscription.CurrentPeriodEnd ? subscription.PlanCode : PlanCode.Free,
            _ => PlanCode.Free
        };
    }

    /// <summary>
    /// Still billing or about to: a second checkout for a plan would charge the tenant twice. A
    /// subscription cancelled at the end of its period is not, so the tenant can pick another.
    /// </summary>
    public static bool IsBilling(TenantSubscription subscription) =>
        subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue or SubscriptionStatus.OnHold && !subscription.CancelAtPeriodEnd;

    /// <summary>A subscription that has not ended.</summary>
    public static bool IsLive(SubscriptionStatus status) =>
        status is SubscriptionStatus.Pending or SubscriptionStatus.Active or SubscriptionStatus.PastDue or SubscriptionStatus.OnHold;

    /// <summary>
    /// The subscription that decides the tenant's plan, of all it has had: the oldest still billing that
    /// was not cancelled as a duplicate; else the newest still live (one cancelled at the end of its
    /// period, or still pending); else the newest of the rest. Null when it has none.
    /// </summary>
    public static TenantSubscription? Deciding(IReadOnlyCollection<TenantSubscription> subscriptions)
    {
        var kept = subscriptions.Where(s => s.DuplicateAt is null).ToList();
        return kept.Where(IsBilling).OrderBy(s => s.CreatedAt).ThenBy(s => s.SubscriptionId, StringComparer.Ordinal).FirstOrDefault()
            ?? kept.Where(s => IsLive(s.Status)).OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.SubscriptionId, StringComparer.Ordinal).FirstOrDefault()
            ?? kept.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.SubscriptionId, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>
    /// Subscriptions billing beside the deciding one: paid for twice, by two checkouts opened at once.
    /// Each is cancelled and its payment refunded, so the tenant is never charged twice.
    /// </summary>
    public static IReadOnlyList<TenantSubscription> Duplicates(IReadOnlyCollection<TenantSubscription> subscriptions)
    {
        TenantSubscription? deciding = Deciding(subscriptions);
        return deciding is null || !IsBilling(deciding)
            ? []
            : subscriptions.Where(s => s.DuplicateAt is null && IsBilling(s) && s.SubscriptionId != deciding.SubscriptionId).ToList();
    }

    /// <summary>
    /// The end of the period a refunded subscription payment made at <paramref name="paidAt"/> paid for,
    /// with a day's slack for the provider's billing date drifting from the payment's time.
    /// </summary>
    public static DateTime RefundedThrough(DateTime paidAt, BillingInterval interval) =>
        (interval == BillingInterval.Yearly ? paidAt.AddYears(1) : paidAt.AddMonths(1)).AddDays(1);
}

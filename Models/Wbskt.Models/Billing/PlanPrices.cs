namespace Wbskt.Models.Billing;

/// <summary>
/// What each self-serve plan costs, in USD (Docs/Billing.md, Plans): the price list the provider's
/// products are checked against at start, and the amount an offer shows. Yearly is two months free.
/// </summary>
public static class PlanPrices
{
    public static readonly IReadOnlyDictionary<(PlanCode Plan, BillingInterval Interval), decimal> Usd =
        new Dictionary<(PlanCode Plan, BillingInterval Interval), decimal>
        {
            [(PlanCode.Maker, BillingInterval.Monthly)] = 5m,
            [(PlanCode.Maker, BillingInterval.Yearly)] = 50m,
            [(PlanCode.Pro, BillingInterval.Monthly)] = 24m,
            [(PlanCode.Pro, BillingInterval.Yearly)] = 240m,
            [(PlanCode.Team, BillingInterval.Monthly)] = 99m,
            [(PlanCode.Team, BillingInterval.Yearly)] = 990m
        };

    /// <summary>The plan's price for the interval, or null when it is not sold self-serve.</summary>
    public static decimal? UsdFor(PlanCode plan, BillingInterval interval) =>
        Usd.TryGetValue((plan, interval), out decimal amount) ? amount : null;
}

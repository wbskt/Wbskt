using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>
/// Payments (<c>Billing:Payments</c>): what this deployment sells through its payment provider, and how
/// long a bought credit pack lasts. The provider's own settings are its options (<see cref="DodoOptions"/>).
/// </summary>
public sealed class BillingOptions
{
    public const string SectionName = "Billing:Payments";

    /// <summary>
    /// The provider's product for each paid plan and interval, such as <c>Plans:Maker:Monthly</c>. A plan
    /// or interval left out is not offered.
    /// </summary>
    public Dictionary<PlanCode, Dictionary<BillingInterval, string>> Plans { get; set; } = [];

    /// <summary>The credit packs offered, by name (<c>CreditPacks:{Name}:ProductId</c> and <c>:Credits</c>).</summary>
    public Dictionary<string, CreditPackOptions> CreditPacks { get; set; } = [];

    /// <summary>How long a bought credit pack can be spent.</summary>
    public TimeSpan CreditPackValidity { get; set; } = TimeSpan.FromDays(365);

    /// <summary>How long a past-due subscription keeps its paid plan before the tenant drops to Free.</summary>
    public TimeSpan PastDueGrace { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// How long a plan checkout stays open for reuse: another request for the same plan in that time gets
    /// the same page, so two tabs or two admins cannot each pay for a subscription.
    /// </summary>
    public TimeSpan CheckoutReuseWindow { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Where the provider sends the buyer back after checkout and from its portal, such as the console's billing page.</summary>
    public string? ReturnUrl { get; set; }

    /// <summary>
    /// The product that sells <paramref name="plan"/> billed <paramref name="interval"/>, or null when it is
    /// not on sale: no product configured, or no price for it in <see cref="PlanPrices"/>.
    /// </summary>
    public string? FindOfferedPlanProduct(PlanCode plan, BillingInterval interval) =>
        PlanPrices.UsdFor(plan, interval) is not null ? FindPlanProduct(plan, interval) : null;

    /// <summary>
    /// The product configured for <paramref name="plan"/> billed <paramref name="interval"/>, on sale or not:
    /// for putting back a change the provider already holds, not for selling.
    /// </summary>
    public string? FindPlanProduct(PlanCode plan, BillingInterval interval) =>
        Plans.TryGetValue(plan, out Dictionary<BillingInterval, string>? intervals)
        && intervals.TryGetValue(interval, out string? productId)
        && !string.IsNullOrEmpty(productId)
            ? productId
            : null;

    /// <summary>The credit pack named <paramref name="name"/>, or null when it is not on sale (<see cref="CreditPackOptions.IsOffered"/>).</summary>
    public CreditPackOptions? FindOfferedCreditPack(string name) =>
        CreditPacks.TryGetValue(name, out CreditPackOptions? pack) && pack.IsOffered ? pack : null;

    /// <summary>The plan and interval <paramref name="productId"/> sells; null when it is not one of <see cref="Plans"/>.</summary>
    public (PlanCode Plan, BillingInterval Interval)? FindPlan(string productId)
    {
        foreach ((PlanCode plan, Dictionary<BillingInterval, string> intervals) in Plans)
        {
            foreach ((BillingInterval interval, string product) in intervals)
            {
                if (!string.IsNullOrEmpty(product) && product == productId)
                {
                    return (plan, interval);
                }
            }
        }

        return null;
    }

    /// <summary>The credit pack <paramref name="productId"/> sells; null when it is not one of <see cref="CreditPacks"/>.</summary>
    public CreditPackOptions? FindCreditPack(string productId) =>
        CreditPacks.Values.FirstOrDefault(p => !string.IsNullOrEmpty(p.ProductId) && p.ProductId == productId);
}

public sealed class CreditPackOptions
{
    public string ProductId { get; set; } = "";

    public decimal Credits { get; set; }

    /// <summary>What the pack costs in USD, as its product is priced at the provider; checked at start.</summary>
    public decimal PriceUsd { get; set; }

    /// <summary>On sale: a product, credits and a price. A pack with no price is not shown as free.</summary>
    public bool IsOffered => !string.IsNullOrEmpty(ProductId) && Credits > 0 && PriceUsd > 0;
}

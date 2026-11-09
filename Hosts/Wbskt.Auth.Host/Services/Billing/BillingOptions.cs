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

    /// <summary>The plan and interval <paramref name="productId"/> sells; null when it is not one of <see cref="Plans"/>.</summary>
    public (PlanCode Plan, BillingInterval Interval)? FindPlan(string productId)
    {
        foreach ((PlanCode plan, Dictionary<BillingInterval, string> intervals) in Plans)
        {
            foreach ((BillingInterval interval, string product) in intervals)
            {
                if (product == productId)
                {
                    return (plan, interval);
                }
            }
        }

        return null;
    }

    /// <summary>The credit pack <paramref name="productId"/> sells; null when it is not one of <see cref="CreditPacks"/>.</summary>
    public CreditPackOptions? FindCreditPack(string productId) =>
        CreditPacks.Values.FirstOrDefault(p => p.ProductId == productId);
}

public sealed class CreditPackOptions
{
    public string ProductId { get; set; } = "";

    public decimal Credits { get; set; }
}

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

    /// <summary>
    /// The provider's product for each credit pack, by its name in <see cref="CreditPackPrices"/>
    /// (<c>CreditPacks:Pack10k:ProductId</c>). A pack left out, or named nowhere in the catalogue, is not offered.
    /// </summary>
    public Dictionary<string, CreditPackOptions> CreditPacks { get; set; } = [];

    /// <summary>How long a bought credit pack can be spent.</summary>
    public TimeSpan CreditPackValidity { get; set; } = TimeSpan.FromDays(365);

    /// <summary>How long a past-due subscription keeps its paid plan before the tenant drops to Free.</summary>
    public TimeSpan PastDueGrace { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// How far back the daily reconcile (<c>BillingReconcileWorker</c>) reads the provider's subscriptions
    /// and payments again. Longer than the provider keeps redelivering a webhook (about a day for Dodo), so
    /// one it gave up on is still in the window, and long enough to cover a reconcile that missed a day.
    /// </summary>
    public TimeSpan ReconcileWindow { get; set; } = TimeSpan.FromDays(3);

    /// <summary>
    /// How far back the reconcile reads the payments behind the provider's disputes. A dispute is listed
    /// by when it was opened, and is won or lost weeks later, so this is far longer than
    /// <see cref="ReconcileWindow"/>.
    /// </summary>
    public TimeSpan DisputeReconcileWindow { get; set; } = TimeSpan.FromDays(120);

    /// <summary>How often the reconcile runs, across every auth replica and restart (<c>dbo.BillingReconciles</c>).</summary>
    public TimeSpan ReconcileEvery { get; set; } = TimeSpan.FromDays(1);

    /// <summary>The pause between one item the reconcile reads and the next, to stay inside the provider's rate limit.</summary>
    public TimeSpan ReconcilePause { get; set; } = TimeSpan.FromMilliseconds(250);

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

    /// <summary>The packs on sale: each configured with a product and priced in <see cref="CreditPackPrices"/>.</summary>
    public IReadOnlyCollection<OfferedCreditPack> OfferedCreditPacks() =>
        CreditPacks
            .Where(p => !string.IsNullOrEmpty(p.Value.ProductId))
            .Select(p => CreditPackPrices.Find(p.Key) is { } pack ? new OfferedCreditPack(p.Key, p.Value.ProductId, pack) : null)
            .OfType<OfferedCreditPack>()
            .ToList();

    /// <summary>The credit pack named <paramref name="name"/>, or null when it is not on sale.</summary>
    public OfferedCreditPack? FindOfferedCreditPack(string name) =>
        OfferedCreditPacks().FirstOrDefault(p => p.Name == name);

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

    /// <summary>The credit pack <paramref name="productId"/> sells; null when it is not one of the <see cref="OfferedCreditPacks"/>.</summary>
    public OfferedCreditPack? FindCreditPack(string productId) =>
        OfferedCreditPacks().FirstOrDefault(p => p.ProductId == productId);
}

public sealed class CreditPackOptions
{
    public string ProductId { get; set; } = "";
}

/// <summary>A credit pack on sale: its catalogue name, its product at the provider, and what it adds and costs.</summary>
public sealed record OfferedCreditPack(string Name, string ProductId, CreditPack Pack);

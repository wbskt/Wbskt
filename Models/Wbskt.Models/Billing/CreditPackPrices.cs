namespace Wbskt.Models.Billing;

/// <summary>
/// The credit packs Wbskt sells, by name, with the credits each adds and what it costs in USD
/// (Docs/Billing.md, Credit packs). Configuration names only each pack's product at the provider, as
/// for plans: the provider's product is checked against this price at start, a payment for it adds
/// these credits, and an offer shows both.
/// </summary>
public static class CreditPackPrices
{
    public static readonly IReadOnlyDictionary<string, CreditPack> Packs =
        new Dictionary<string, CreditPack>(StringComparer.Ordinal)
        {
            ["Pack10k"] = new(10_000m, 5m)
        };

    /// <summary>The pack named <paramref name="name"/>, or null when Wbskt sells none by that name.</summary>
    public static CreditPack? Find(string name) => Packs.GetValueOrDefault(name);
}

/// <param name="Credits">The workflow credits one pack adds.</param>
/// <param name="Usd">What one pack costs.</param>
public sealed record CreditPack(decimal Credits, decimal Usd);

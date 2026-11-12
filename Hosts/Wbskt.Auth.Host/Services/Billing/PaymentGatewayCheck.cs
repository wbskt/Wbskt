using Microsoft.Extensions.Options;
using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>
/// Once at start, asks the payment provider whether it accepts this deployment's credentials, so a key
/// for the wrong mode (a live key against the test API) shows up in the logs at deploy rather than at a
/// customer's first checkout. Then reads every product configured for a plan or a credit pack and checks
/// it is priced as <see cref="PlanPrices"/> and the pack's <c>PriceUsd</c> say, in USD and for the right
/// interval, so a product id set to another plan's product, or a price changed in the dashboard, is caught
/// before a customer pays one price for another plan's limits. Never stops the host: payments stay as
/// configured either way.
/// </summary>
public sealed class PaymentGatewayCheck(
    IServiceScopeFactory scopeFactory,
    IOptions<DodoOptions> dodo,
    IOptions<BillingOptions> billing,
    ILogger<PaymentGatewayCheck> logger) : BackgroundService
{
    private const string Currency = "USD";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var gateway = scope.ServiceProvider.GetRequiredService<IPaymentGateway>();
        if (!gateway.IsConfigured)
        {
            logger.LogInformation("Payments are off: {Provider} has no API key and webhook secret here.", gateway.Name);
            return;
        }

        try
        {
            if (!await gateway.AcceptsCredentialsAsync(stoppingToken))
            {
                logger.LogCritical(
                    "{Provider} refuses this deployment's API key in {Mode}: every checkout will fail. Is it a key for the other mode?",
                    gateway.Name, dodo.Value.Environment);
                return;
            }

            logger.LogInformation("{Provider} accepts this deployment's API key in {Mode}.", gateway.Name, dodo.Value.Environment);
            foreach (string problem in await FindPriceProblemsAsync(gateway, billing.Value, stoppingToken))
            {
                logger.LogCritical("{Provider} prices a product other than the plan catalogue does: {Problem}", gateway.Name, problem);
            }
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not reach {Provider} to check its API key and prices; checkouts will tell.", gateway.Name);
        }
    }

    /// <summary>Each configured product the provider prices differently from the catalogue, described for the log.</summary>
    internal static async Task<IReadOnlyCollection<string>> FindPriceProblemsAsync(IPaymentGateway gateway, BillingOptions options, CancellationToken ct)
    {
        var problems = new List<string>();
        foreach ((PlanCode plan, Dictionary<BillingInterval, string> intervals) in options.Plans)
        {
            foreach ((BillingInterval interval, string productId) in intervals)
            {
                if (string.IsNullOrEmpty(productId))
                {
                    continue;
                }

                string what = $"{plan} {interval} ({productId})";
                if (PlanPrices.UsdFor(plan, interval) is not { } expected)
                {
                    problems.Add($"{what} is configured, but the catalogue has no price for it, so it is not offered.");
                    continue;
                }

                ProviderProduct? product = await gateway.FindProductAsync(productId, ct);
                problems.AddRange(Compare(what, product, expected, interval));
            }
        }

        foreach ((string name, CreditPackOptions pack) in options.CreditPacks)
        {
            if (string.IsNullOrEmpty(pack.ProductId))
            {
                continue;
            }

            string what = $"credit pack {name} ({pack.ProductId})";
            if (pack.PriceUsd <= 0)
            {
                problems.Add($"{what} has no PriceUsd configured, so it shows as free.");
                continue;
            }

            ProviderProduct? product = await gateway.FindProductAsync(pack.ProductId, ct);
            problems.AddRange(Compare(what, product, pack.PriceUsd, interval: null));
        }

        return problems;
    }

    // A plan is a subscription charged every interval; a pack (interval null) is charged once.
    private static IEnumerable<string> Compare(string what, ProviderProduct? product, decimal expected, BillingInterval? interval)
    {
        if (product is null)
        {
            yield return $"{what} does not exist at the provider.";
            yield break;
        }

        if (!string.Equals(product.Currency, Currency, StringComparison.OrdinalIgnoreCase) || product.Amount != expected)
        {
            yield return $"{what} costs {product.Amount} {product.Currency}, not {expected} {Currency}.";
        }

        if (product.Recurring != interval.HasValue)
        {
            yield return interval is null ? $"{what} is a subscription, not a one-time purchase." : $"{what} is a one-time purchase, not a subscription.";
        }
        else if (interval is not null && product.Interval != interval)
        {
            yield return $"{what} is billed {product.Interval?.ToString() ?? "on another period"}, not {interval}.";
        }
    }
}

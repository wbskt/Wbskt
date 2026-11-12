using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>
/// Dodo Payments over its REST API, with the API key as a bearer token. Every read is fresh: the sync
/// acts on what Dodo says now, not on a webhook's body, which can arrive late, twice or out of order.
/// </summary>
public sealed class DodoPaymentGateway(HttpClient http, IOptions<DodoOptions> options, TimeProvider clock, ILogger<DodoPaymentGateway> logger) : IPaymentGateway
{
    public const string ProviderName = "dodo";

    /// <summary>The metadata key a checkout carries the tenant's reference in.</summary>
    internal const string TenantRefKey = "tenant_ref";

    private const int ListPageSize = 100;
    private const int MaxListPages = 50;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Name => ProviderName;

    public bool IsConfigured => options.Value is { ApiKey: not null, WebhookSecret: not null };

    public async Task<string> CreateCheckoutAsync(BillingCheckout checkout, CancellationToken ct)
    {
        // Attached to the tenant's own customer, or a new one: never left to Dodo to match by email.
        string? customerId = checkout.CustomerId is { Length: > 0 } id ? id : null;
        var request = new DodoCheckoutRequest(
            [new DodoCartItem(checkout.ProductId, 1)],
            customerId is not null ? new DodoExistingCustomer(customerId) : new DodoCheckoutCustomer(checkout.Email, checkout.Name),
            new Dictionary<string, string> { [TenantRefKey] = checkout.TenantRef.ToString() },
            checkout.ReturnUrl,
            customerId is not null ? null : new DodoCheckoutFeatureFlags(AlwaysCreateNewCustomer: true));

        using HttpResponseMessage response = await http.PostAsJsonAsync("checkouts", request, Json, ct);
        DodoCheckoutResponse? body = await ReadAsync<DodoCheckoutResponse>(response, "create a checkout", ct);
        return body?.CheckoutUrl ?? throw new InvalidOperationException("Dodo answered a checkout without a checkout_url.");
    }

    public async Task<string> CreatePortalLinkAsync(string customerId, string? returnUrl, CancellationToken ct)
    {
        string path = $"customers/{Uri.EscapeDataString(customerId)}/customer-portal/session";
        if (returnUrl is not null)
        {
            path += "?return_url=" + Uri.EscapeDataString(returnUrl);
        }

        using HttpResponseMessage response = await http.PostAsync(path, content: null, ct);
        DodoPortalResponse? body = await ReadAsync<DodoPortalResponse>(response, "open a customer portal", ct);
        return body?.Link ?? throw new InvalidOperationException("Dodo answered a portal session without a link.");
    }

    public async Task<ProviderSubscription?> FindSubscriptionAsync(string subscriptionId, CancellationToken ct)
    {
        using HttpResponseMessage response = await http.GetAsync($"subscriptions/{Uri.EscapeDataString(subscriptionId)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        DodoSubscription s = await ReadAsync<DodoSubscription>(response, "read a subscription", ct)
            ?? throw new InvalidOperationException("Dodo answered a subscription with no body.");
        if (!SubscriptionStatusNames.TryParse(s.Status, out SubscriptionStatus status))
        {
            // A status added after this was written changes nothing until it is understood.
            logger.LogWarning("Dodo subscription {SubscriptionId} has status {Status}, which is not known here; treating it as pending.", subscriptionId, s.Status);
            status = SubscriptionStatus.Pending;
        }

        return new ProviderSubscription(
            s.SubscriptionId ?? subscriptionId,
            status,
            s.ProductId ?? "",
            s.Customer?.CustomerId ?? "",
            ReadTenantRef(s.Metadata),
            s.NextBillingDate ?? clock.GetUtcNow(),
            s.CancelAtNextBillingDate ?? false,
            s.ScheduledChange is { ProductId: { Length: > 0 } product, EffectiveAt: { } effectiveAt } ? new ProviderScheduledChange(product, effectiveAt) : null);
    }

    public async Task<ProviderPayment?> FindPaymentAsync(string paymentId, CancellationToken ct)
    {
        using HttpResponseMessage response = await http.GetAsync($"payments/{Uri.EscapeDataString(paymentId)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        DodoPayment p = await ReadAsync<DodoPayment>(response, "read a payment", ct)
            ?? throw new InvalidOperationException("Dodo answered a payment with no body.");
        return new ProviderPayment(
            p.PaymentId ?? paymentId,
            p.Status == "succeeded",
            p.Customer?.CustomerId ?? "",
            ReadTenantRef(p.Metadata),
            string.IsNullOrEmpty(p.SubscriptionId) ? null : p.SubscriptionId,
            (p.ProductCart ?? []).Select(i => new PaymentLine(i.ProductId, i.Quantity)).ToList(),
            p.RefundStatus == "full",
            p.CreatedAt ?? clock.GetUtcNow(),
            ReadDispute(p.Disputes));
    }

    // A pending subscription is a checkout not paid for: nothing to reconcile until a payment names it.
    public async Task<ProviderIdList> ListSubscriptionIdsAsync(DateTimeOffset createdFrom, CancellationToken ct) =>
        await ListIdsAsync("subscriptions", createdFrom, item => item.Status == "pending" ? null : item.SubscriptionId, ct);

    public async Task<ProviderIdList> ListPaymentIdsAsync(DateTimeOffset createdFrom, CancellationToken ct) =>
        await ListIdsAsync("payments", createdFrom, item => item.PaymentId, ct);

    public async Task<ProviderProduct?> FindProductAsync(string productId, CancellationToken ct)
    {
        using HttpResponseMessage response = await http.GetAsync($"products/{Uri.EscapeDataString(productId)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        DodoProduct product = await ReadAsync<DodoProduct>(response, "read a product", ct)
            ?? throw new InvalidOperationException("Dodo answered a product with no body.");
        DodoPrice price = product.Price ?? throw new InvalidOperationException($"Dodo's product {productId} has no price.");
        long minor = price.Price ?? price.FixedPrice ?? throw new InvalidOperationException($"Dodo's product {productId} has no amount.");
        BillingInterval? interval = (price.PaymentFrequencyCount, price.PaymentFrequencyInterval?.ToLowerInvariant()) switch
        {
            (1, "month") => BillingInterval.Monthly,
            (1, "year") or (12, "month") => BillingInterval.Yearly,
            _ => null
        };

        // The amount is read as two decimal places, which every currency the plans are priced in has;
        // another currency is a mismatch either way.
        return new ProviderProduct(
            minor / 100m,
            price.Currency ?? "",
            product.IsRecurring,
            product.IsRecurring ? interval : null,
            Discounted: price.Discount is > 0 || price.DiscountBps is > 0,
            TrialDays: price.TrialPeriodDays ?? 0,
            BuyerSetsAmount: price.PayWhatYouWant == true,
            UsageBased: price.Type == "usage_based_price");
    }

    public async Task<bool> AcceptsCredentialsAsync(CancellationToken ct)
    {
        using HttpResponseMessage response = await http.GetAsync("customers?page_size=1", ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return false;
        }

        EnsureSucceeded(response, "list customers");
        return true;
    }

    public async Task CancelSubscriptionAsync(string subscriptionId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"subscriptions/{Uri.EscapeDataString(subscriptionId)}")
        {
            Content = JsonContent.Create(new DodoSubscriptionUpdate("cancelled"), options: Json)
        };
        using HttpResponseMessage response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // A stored copy can still say billing after an earlier cancel went through: one already ended (or
        // unknown to Dodo) is what was asked for, so a refusal of it is not a failure.
        if (await FindSubscriptionAsync(subscriptionId, ct) is not { } current || !SubscriptionPlan.IsLive(current.Status))
        {
            return;
        }

        EnsureSucceeded(response, "cancel a subscription");
    }

    public async Task ChangePlanAsync(string subscriptionId, string productId, PlanChangeTiming timing, CancellationToken ct)
    {
        // An upgrade charges the prorated difference now and changes nothing if that charge fails; a
        // downgrade waits for the next billing date, which then bills the new plan in full, and replaces
        // a downgrade already scheduled.
        DodoChangePlanRequest request = timing == PlanChangeTiming.Now
            ? new DodoChangePlanRequest(productId, 1, "prorated_immediately", "immediately", "prevent_change", null)
            : new DodoChangePlanRequest(productId, 1, "full_immediately", "next_billing_date", null, true);

        using HttpResponseMessage response = await http.PostAsJsonAsync($"subscriptions/{Uri.EscapeDataString(subscriptionId)}/change-plan", request, Json, ct);
        EnsureSucceeded(response, "change a subscription's plan");
    }

    public async Task CancelScheduledChangeAsync(string subscriptionId, CancellationToken ct)
    {
        using HttpResponseMessage response = await http.DeleteAsync($"subscriptions/{Uri.EscapeDataString(subscriptionId)}/change-plan/scheduled", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        EnsureSucceeded(response, "cancel a scheduled plan change");
    }

    public async Task RefundPaymentAsync(string paymentId, string reason, CancellationToken ct)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync("refunds", new DodoRefundRequest(paymentId, reason), Json, ct);
        EnsureSucceeded(response, "refund a payment");
    }

    public ProviderWebhook? ReadWebhook(IReadOnlyDictionary<string, string> headers, string body)
    {
        if (options.Value.WebhookSecret is not { } secret
            || !headers.TryGetValue("webhook-id", out string? id)
            || !headers.TryGetValue("webhook-timestamp", out string? timestamp)
            || !headers.TryGetValue("webhook-signature", out string? signature)
            || !StandardWebhookSignature.IsValid(secret, id, timestamp, body, signature, clock.GetUtcNow()))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            string type = root.TryGetProperty("type", out JsonElement t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "";
            JsonElement data = root.TryGetProperty("data", out JsonElement d) && d.ValueKind == JsonValueKind.Object ? d : default;
            return new ProviderWebhook(id, type, ReadString(data, "subscription_id"), ReadString(data, "payment_id"));
        }
        catch (JsonException)
        {
            // Signed by Dodo but not JSON: nothing here can act on it.
            return new ProviderWebhook(id, "", null, null);
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } s
            ? s
            : null;

    /// <summary>Where the payment's latest dispute stands, by Dodo's <c>dispute_status</c>.</summary>
    private static PaymentDispute ReadDispute(IReadOnlyList<DodoDispute>? disputes) =>
        disputes?.OrderBy(d => d.CreatedAt).LastOrDefault()?.DisputeStatus switch
        {
            "dispute_opened" or "dispute_challenged" => PaymentDispute.Open,
            "dispute_lost" or "dispute_accepted" or "dispute_expired" => PaymentDispute.Lost,
            _ => PaymentDispute.None
        };

    private static Guid? ReadTenantRef(Dictionary<string, JsonElement>? metadata) =>
        metadata is not null && metadata.TryGetValue(TenantRefKey, out JsonElement value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out Guid tenantRef)
            ? tenantRef
            : null;

    // Dodo pages by number (from 0, at most 100 a page) and says nothing of a next page: a short page is
    // the last. The pages read are capped, so a window far longer than intended cannot run on for ever.
    // Items made while it pages can shift one onto the next page, so ids are kept once.
    private async Task<ProviderIdList> ListIdsAsync(string path, DateTimeOffset createdFrom, Func<DodoListItem, string?> id, CancellationToken ct)
    {
        string from = Uri.EscapeDataString(createdFrom.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int page = 0; page < MaxListPages; page++)
        {
            using HttpResponseMessage response = await http.GetAsync($"{path}?created_at_gte={from}&page_size={ListPageSize}&page_number={page}", ct);
            DodoList? body = await ReadAsync<DodoList>(response, $"list {path}", ct);

            // A page without items is not an empty list: the answer is not the one expected.
            IReadOnlyList<DodoListItem> items = body?.Items ?? throw new InvalidOperationException($"Dodo's list of {path} had no items field.");
            ids.UnionWith(items.Select(id).OfType<string>().Where(i => i.Length > 0));
            if (items.Count < ListPageSize)
            {
                return new ProviderIdList(ids, Complete: true);
            }
        }

        logger.LogWarning("Listed {Pages} pages of Dodo {Path} since {From} and stopped; the rest are not reconciled this time.", MaxListPages, path, createdFrom);
        return new ProviderIdList(ids, Complete: false);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, string what, CancellationToken ct)
    {
        EnsureSucceeded(response, what);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct);
    }

    private static void EnsureSucceeded(HttpResponseMessage response, string what)
    {
        if (!response.IsSuccessStatusCode)
        {
            // The body is Dodo's, and may name the customer; the status is enough to act on.
            throw new HttpRequestException($"Dodo refused to {what}: {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);
        }
    }
}

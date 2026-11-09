using Microsoft.Extensions.Options;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Models;
using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Services.Billing;

internal sealed class TenantBillingService(
    IPaymentGateway gateway,
    ITenantBillingProvider billing,
    ITenantPlanProvider plans,
    IAccountProvider accounts,
    IOptions<BillingOptions> options,
    TimeProvider clock,
    ILogger<TenantBillingService> logger) : ITenantBillingService
{
    private static readonly Error TenantNotFound = Error.Forbidden("TENANT_NOT_FOUND", "Tenant not found.");
    private static readonly Error NotConfigured = Error.Unavailable("BILLING_UNAVAILABLE", "Payments are not set up on this deployment.");
    private static readonly Error ProviderDown = Error.Unavailable("BILLING_PROVIDER_UNAVAILABLE", "The payment provider could not be reached. Try again shortly.");

    public async Task<Result<Page<CreditPurchaseResponse>>> GetCreditPurchasesAsync(PageRequest page, CancellationToken ct)
    {
        Result<long?> after = page.AfterKey();
        if (after.IsFailure)
        {
            return after.Error;
        }

        if (after.Value is > int.MaxValue or < 0)
        {
            return PageRequest.CursorInvalid;
        }

        int take = page.LimitOr(Paging.MaxPageSize);
        IReadOnlyCollection<CreditPurchaseResponse> rows = await billing.GetCreditPurchasesAsync((int?)after.Value, take, ct);
        return new Page<CreditPurchaseResponse>
        {
            Items = rows,
            NextCursor = rows.Count == take ? PageRequest.KeyCursor(rows.Last().Id) : null
        };
    }

    public async Task<Result<TenantBillingResponse>> GetAsync(int tenantId, CancellationToken ct)
    {
        if (await plans.FindByIdAsync(tenantId, ct) is not { } tenant)
        {
            return TenantNotFound;
        }

        TenantSubscription? subscription = SubscriptionPlan.Deciding(await billing.GetSubscriptionsAsync(tenantId, ct));
        BillingOptions offers = options.Value;
        return new TenantBillingResponse(
            tenant.PlanCode,
            subscription is null ? null : new TenantSubscriptionResponse(
                subscription.PlanCode,
                subscription.Interval,
                subscription.Status,
                subscription.CurrentPeriodEnd.ToUtcOffset(),
                subscription.CancelAtPeriodEnd,
                subscription.PastDueSince?.ToUtcOffset()),
            gateway.IsConfigured
                ? offers.Plans.SelectMany(p => p.Value.Where(i => !string.IsNullOrEmpty(i.Value)).Select(i => new PlanOffer(p.Key, i.Key))).OrderBy(o => o.Plan).ThenBy(o => o.Interval).ToList()
                : [],
            gateway.IsConfigured
                ? offers.CreditPacks.Where(p => !string.IsNullOrEmpty(p.Value.ProductId) && p.Value.Credits > 0).Select(p => new CreditPackOffer(p.Key, p.Value.Credits)).OrderBy(o => o.Credits).ToList()
                : []);
    }

    public async Task<Result<BillingCheckoutResponse>> CheckoutAsync(int tenantId, int userId, BillingCheckoutRequest request, CancellationToken ct)
    {
        if (!gateway.IsConfigured)
        {
            return NotConfigured;
        }

        Result<string> product = await ResolveProductAsync(tenantId, request, ct);
        if (product.IsFailure)
        {
            return product.Error;
        }

        if (await plans.FindByIdAsync(tenantId, ct) is not { } tenant)
        {
            return TenantNotFound;
        }

        // A plan checkout still open is handed out again, so two tabs or two admins pay for one subscription.
        DateTime now = clock.GetUtcNow().UtcDateTime;
        bool plan = request.Plan is not null;
        if (plan && await billing.FindOpenCheckoutAsync(tenantId, gateway.Name, product.Value, now, ct) is { } open)
        {
            return new BillingCheckoutResponse(open);
        }

        User buyer = await accounts.GetByIdAsync(userId, ct);
        try
        {
            string url = await gateway.CreateCheckoutAsync(new BillingCheckout(product.Value, tenant.TenantRefId, buyer.Email, buyer.Username, options.Value.ReturnUrl), ct);
            if (plan)
            {
                await billing.RecordCheckoutAsync(tenantId, gateway.Name, product.Value, url, now, now + options.Value.CheckoutReuseWindow, ct);
            }

            logger.LogInformation("User {UserId} started a {Provider} checkout for product {ProductId} for tenant {TenantId}.", userId, gateway.Name, product.Value, tenantId);
            return new BillingCheckoutResponse(url);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "{Provider} refused or could not take a checkout for tenant {TenantId}.", gateway.Name, tenantId);
            return ProviderDown;
        }
    }

    public async Task<Result<BillingPortalResponse>> PortalAsync(int tenantId, CancellationToken ct)
    {
        if (!gateway.IsConfigured)
        {
            return NotConfigured;
        }

        if (await billing.FindCustomerIdAsync(tenantId, gateway.Name, ct) is not { } customerId)
        {
            return Error.NotFound("BILLING_CUSTOMER_NOT_FOUND", "The tenant has not paid for anything yet, so it has no billing portal.");
        }

        try
        {
            return new BillingPortalResponse(await gateway.CreatePortalLinkAsync(customerId, options.Value.ReturnUrl, ct));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "{Provider} refused or could not open a portal for tenant {TenantId}.", gateway.Name, tenantId);
            return ProviderDown;
        }
    }

    /// <summary>The product a checkout request asks for, if this deployment offers it and the tenant can buy it now.</summary>
    private async Task<Result<string>> ResolveProductAsync(int tenantId, BillingCheckoutRequest request, CancellationToken ct)
    {
        BillingOptions offers = options.Value;
        if ((request.Plan is null) == (request.CreditPack is null))
        {
            return Error.Validation("BILLING_CHECKOUT_INVALID", "Name either a 'plan' with its 'interval', or a 'creditPack'.");
        }

        if (request.CreditPack is { } pack)
        {
            return offers.CreditPacks.TryGetValue(pack, out CreditPackOptions? packOptions) && !string.IsNullOrEmpty(packOptions.ProductId) && packOptions.Credits > 0
                ? packOptions.ProductId
                : Error.Validation("BILLING_OFFER_INVALID", $"Credit pack '{pack}' is not offered.");
        }

        if (request.Interval is not { } interval)
        {
            return Error.Validation("BILLING_CHECKOUT_INVALID", "A plan needs an 'interval': Monthly or Yearly.");
        }

        if (!offers.Plans.TryGetValue(request.Plan!.Value, out var intervals) || !intervals.TryGetValue(interval, out string? productId) || string.IsNullOrEmpty(productId))
        {
            return Error.Validation("BILLING_OFFER_INVALID", $"{request.Plan} billed {interval} is not offered.");
        }

        if (SubscriptionPlan.Deciding(await billing.GetSubscriptionsAsync(tenantId, ct)) is { } current && SubscriptionPlan.IsBilling(current))
        {
            return Error.Conflict("BILLING_SUBSCRIPTION_ACTIVE", "The tenant already has a subscription. Change or cancel it in the billing portal.");
        }

        return productId;
    }
}

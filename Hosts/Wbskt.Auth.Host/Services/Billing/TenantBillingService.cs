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
    TenantBillingSync sync,
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
                subscription.PastDueSince?.ToUtcOffset(),
                subscription.ScheduledPlan,
                subscription.ScheduledInterval,
                subscription.ScheduledAt?.ToUtcOffset()),
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

    public async Task<Result<TenantBillingResponse>> ChangePlanAsync(int tenantId, ChangePlanRequest request, CancellationToken ct)
    {
        if (!gateway.IsConfigured)
        {
            return NotConfigured;
        }

        if (!options.Value.Plans.TryGetValue(request.Plan, out var intervals) || !intervals.TryGetValue(request.Interval, out string? productId) || string.IsNullOrEmpty(productId))
        {
            return Error.Validation("BILLING_OFFER_INVALID", $"{request.Plan} billed {request.Interval} is not offered.");
        }

        if (SubscriptionPlan.Deciding(await billing.GetSubscriptionsAsync(tenantId, ct)) is not { } current || !SubscriptionPlan.IsBilling(current))
        {
            return Error.Conflict("BILLING_SUBSCRIPTION_REQUIRED", "The tenant has no subscription to change. Start one with a checkout.");
        }

        if (current.CancelAtPeriodEnd || current.PastDueSince is not null)
        {
            return Error.Conflict(
                "BILLING_SUBSCRIPTION_NOT_CHANGEABLE",
                "The subscription is cancelled or has a failed payment. Resume it or settle the payment in the billing portal first.");
        }

        bool same = current.PlanCode == request.Plan && current.Interval == request.Interval;
        bool upgrade = request.Plan > current.PlanCode
            || (request.Plan == current.PlanCode && current.Interval == BillingInterval.Monthly && request.Interval == BillingInterval.Yearly);
        try
        {
            if (same)
            {
                if (current.ScheduledPlan is null)
                {
                    return await GetAsync(tenantId, ct);
                }

                await gateway.CancelScheduledChangeAsync(current.SubscriptionId, ct);
            }
            else if (upgrade)
            {
                await UpgradeAsync(current, productId, ct);
            }
            else
            {
                await gateway.ChangePlanAsync(current.SubscriptionId, productId, PlanChangeTiming.AtPeriodEnd, ct);
            }

        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "{Provider} refused or could not change subscription {SubscriptionId} of tenant {TenantId}.", gateway.Name, current.SubscriptionId, tenantId);
            return ProviderDown;
        }

        // The provider's webhooks follow, but the caller sees the change now. A failed upgrade charge
        // leaves the subscription on its plan, and the read says so. The change is made either way, so a
        // read that fails here is left to the webhook rather than answered as a failure to be retried.
        try
        {
            await sync.SyncSubscriptionAsync(current.SubscriptionId, refundedThrough: null, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Subscription {SubscriptionId} of tenant {TenantId} was changed but could not be read back; its webhook will bring it in.", current.SubscriptionId, tenantId);
        }

        logger.LogInformation(
            "Tenant {TenantId} asked to move subscription {SubscriptionId} from {Plan} {Interval} to {NewPlan} {NewInterval} ({Timing}).",
            tenantId, current.SubscriptionId, current.PlanCode, current.Interval, request.Plan, request.Interval,
            same ? "scheduled change dropped" : upgrade ? "now" : "at period end");
        return await GetAsync(tenantId, ct);
    }

    /// <summary>
    /// Moves the subscription up at once. A downgrade still scheduled would undo the upgrade at the
    /// period's end, so it is dropped first, and scheduled again if the upgrade does not go through, so
    /// the customer does not renew on the plan they asked to leave.
    /// </summary>
    private async Task UpgradeAsync(TenantSubscription current, string productId, CancellationToken ct)
    {
        if (current.ScheduledPlan is not { } scheduledPlan || current.ScheduledInterval is not { } scheduledInterval)
        {
            await gateway.ChangePlanAsync(current.SubscriptionId, productId, PlanChangeTiming.Now, ct);
            return;
        }

        await gateway.CancelScheduledChangeAsync(current.SubscriptionId, ct);
        try
        {
            await gateway.ChangePlanAsync(current.SubscriptionId, productId, PlanChangeTiming.Now, ct);
        }
        catch (HttpRequestException)
        {
            if (options.Value.Plans.TryGetValue(scheduledPlan, out var intervals) && intervals.TryGetValue(scheduledInterval, out string? scheduledProduct) && !string.IsNullOrEmpty(scheduledProduct))
            {
                await RescheduleAsync(current, scheduledProduct);
            }

            throw;
        }
    }

    private async Task RescheduleAsync(TenantSubscription current, string scheduledProduct)
    {
        try
        {
            await gateway.ChangePlanAsync(current.SubscriptionId, scheduledProduct, PlanChangeTiming.AtPeriodEnd, CancellationToken.None);
            logger.LogWarning("Upgrade of subscription {SubscriptionId} of tenant {TenantId} failed; its scheduled move to {Plan} {Interval} was put back.",
                current.SubscriptionId, current.TenantId, current.ScheduledPlan, current.ScheduledInterval);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Upgrade of subscription {SubscriptionId} of tenant {TenantId} failed and its scheduled move to {Plan} {Interval} could not be put back; it renews on {Current}.",
                current.SubscriptionId, current.TenantId, current.ScheduledPlan, current.ScheduledInterval, current.PlanCode);
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

        TenantSubscription? current = SubscriptionPlan.Deciding(await billing.GetSubscriptionsAsync(tenantId, ct));
        if (current is not null && SubscriptionPlan.IsBilling(current))
        {
            return Error.Conflict("BILLING_SUBSCRIPTION_ACTIVE", "The tenant already has a subscription. Change its plan with POST …/billing/plan, or cancel it in the billing portal.");
        }

        // Cancelled at the end of a period still paid for: a second subscription would bill for the same days.
        if (current is { Status: SubscriptionStatus.Active or SubscriptionStatus.PastDue or SubscriptionStatus.OnHold, CancelAtPeriodEnd: true })
        {
            return Error.Conflict(
                "BILLING_SUBSCRIPTION_ACTIVE",
                $"The tenant's subscription is paid for until {current.CurrentPeriodEnd:yyyy-MM-dd} and ends then. Revoke the cancellation in the billing portal and change its plan, or check out once it has ended.");
        }

        return productId;
    }
}

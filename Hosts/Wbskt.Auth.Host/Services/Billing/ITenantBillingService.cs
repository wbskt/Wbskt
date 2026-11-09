using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Models;

namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>A tenant's subscription and purchases: what it is on, what it can buy, and the provider's pages to pay or manage it.</summary>
public interface ITenantBillingService
{
    Task<Result<TenantBillingResponse>> GetAsync(int tenantId, CancellationToken ct);

    /// <summary>Starts the provider's checkout for a plan or a credit pack, for the signed-in <paramref name="userId"/> to pay.</summary>
    Task<Result<BillingCheckoutResponse>> CheckoutAsync(int tenantId, int userId, BillingCheckoutRequest request, CancellationToken ct);

    /// <summary>The provider's customer portal, once the tenant has paid for something.</summary>
    Task<Result<BillingPortalResponse>> PortalAsync(int tenantId, CancellationToken ct);

    /// <summary>Every credit pack bought, a page at a time by id, for the main database's reconcile of its grants.</summary>
    Task<Result<Page<CreditPurchaseResponse>>> GetCreditPurchasesAsync(PageRequest page, CancellationToken ct);
}

/// <summary>Receives a payment provider's webhooks.</summary>
public interface IBillingWebhookService
{
    /// <summary>
    /// Verifies, stores and acts on one delivery. A delivery already acted on is answered without acting
    /// again. A failure while acting on it propagates, so the provider delivers it again.
    /// </summary>
    Task<Result> ReceiveAsync(string provider, IReadOnlyDictionary<string, string> headers, string body, CancellationToken ct);
}

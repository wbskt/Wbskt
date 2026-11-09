using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>
/// A payment provider's API: its hosted checkout and portal, what it says about a subscription or a
/// payment now, and its signed webhooks. Plans, limits and credits stay here; the provider only takes
/// money and says what was paid for. <see cref="DodoPaymentGateway"/> is the first.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The provider's name in the webhook route and the stored rows, such as <c>dodo</c>.</summary>
    string Name { get; }

    /// <summary>False when the deployment has no credentials for it: checkout and webhooks are then refused.</summary>
    bool IsConfigured { get; }

    /// <summary>Starts a hosted checkout for <paramref name="checkout"/>; returns the page to send the buyer to.</summary>
    Task<string> CreateCheckoutAsync(BillingCheckout checkout, CancellationToken ct);

    /// <summary>A link to the provider's customer portal for <paramref name="customerId"/>, which leads back to <paramref name="returnUrl"/>.</summary>
    Task<string> CreatePortalLinkAsync(string customerId, string? returnUrl, CancellationToken ct);

    Task<ProviderSubscription?> FindSubscriptionAsync(string subscriptionId, CancellationToken ct);

    Task<ProviderPayment?> FindPaymentAsync(string paymentId, CancellationToken ct);

    /// <summary>
    /// Checks a delivered webhook's signature over its raw body and reads it; null when the signature or
    /// its timestamp does not hold up.
    /// </summary>
    ProviderWebhook? ReadWebhook(IReadOnlyDictionary<string, string> headers, string body);
}

/// <summary>
/// A checkout for one product. <c>TenantRef</c> travels in the provider's metadata, so its webhooks can
/// be traced back to the tenant; <c>ReturnUrl</c> is where the buyer lands after paying.
/// </summary>
public sealed record BillingCheckout(string ProductId, Guid TenantRef, string Email, string? Name, string? ReturnUrl);

/// <summary>
/// A subscription as the provider reports it. <c>TenantRef</c> is the tenant its checkout was for, from
/// the metadata; <c>PeriodEnd</c> is the next billing date, the end of what has been paid for.
/// </summary>
public sealed record ProviderSubscription(
    string Id,
    SubscriptionStatus Status,
    string ProductId,
    string CustomerId,
    Guid? TenantRef,
    DateTimeOffset PeriodEnd,
    bool CancelAtPeriodEnd);

/// <summary>
/// A payment as the provider reports it. <c>SubscriptionId</c> is the subscription it paid for, null for
/// a one-time purchase; <c>Lines</c> is what it bought; <c>FullyRefunded</c> is true once all of it has
/// been refunded.
/// </summary>
public sealed record ProviderPayment(
    string Id,
    bool Succeeded,
    string CustomerId,
    Guid? TenantRef,
    string? SubscriptionId,
    IReadOnlyList<PaymentLine> Lines,
    bool FullyRefunded,
    DateTimeOffset CreatedAt);

public sealed record PaymentLine(string ProductId, int Quantity);

/// <summary>A verified webhook: its id (for idempotency), its type, and what it names.</summary>
public sealed record ProviderWebhook(string EventId, string Type, string? SubscriptionId, string? PaymentId);

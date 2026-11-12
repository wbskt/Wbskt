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
    /// The subscriptions made at or after <paramref name="createdFrom"/>, by id, for the reconcile that
    /// reads each one again in case its webhooks were never delivered. One never paid for (a checkout
    /// left open) is left out; a payment that names it still reads it, as that payment's webhook would.
    /// </summary>
    Task<ProviderIdList> ListSubscriptionIdsAsync(DateTimeOffset createdFrom, CancellationToken ct);

    /// <summary>The payments made at or after <paramref name="createdFrom"/>, by id, for the same reconcile.</summary>
    Task<ProviderIdList> ListPaymentIdsAsync(DateTimeOffset createdFrom, CancellationToken ct);

    /// <summary>
    /// Whether the provider accepts this deployment's credentials, by one harmless read. False when it
    /// refuses them, as it does a live key sent to its test API; throws when it cannot be reached.
    /// </summary>
    Task<bool> AcceptsCredentialsAsync(CancellationToken ct);

    /// <summary>How the provider prices <paramref name="productId"/> now; null when it has no such product.</summary>
    Task<ProviderProduct?> FindProductAsync(string productId, CancellationToken ct);

    /// <summary>Cancels the subscription now, so it never charges again. Cancelling one already ended is harmless.</summary>
    Task CancelSubscriptionAsync(string subscriptionId, CancellationToken ct);

    /// <summary>
    /// Moves the subscription to <paramref name="productId"/>: at once, charging the prorated difference
    /// and leaving the plan as it was when that charge fails, or at the end of the period paid for,
    /// replacing any change already scheduled.
    /// </summary>
    Task ChangePlanAsync(string subscriptionId, string productId, PlanChangeTiming timing, CancellationToken ct);

    /// <summary>Drops a change scheduled for the end of the period. Dropping none is harmless.</summary>
    Task CancelScheduledChangeAsync(string subscriptionId, CancellationToken ct);

    /// <summary>Refunds the whole of the payment, giving <paramref name="reason"/> to the provider.</summary>
    Task RefundPaymentAsync(string paymentId, string reason, CancellationToken ct);

    /// <summary>
    /// Checks a delivered webhook's signature over its raw body and reads it; null when the signature or
    /// its timestamp does not hold up.
    /// </summary>
    ProviderWebhook? ReadWebhook(IReadOnlyDictionary<string, string> headers, string body);
}

/// <summary>
/// A checkout for one product. <c>TenantRef</c> travels in the provider's metadata, so its webhooks can
/// be traced back to the tenant; <c>ReturnUrl</c> is where the buyer lands after paying. <c>CustomerId</c>
/// is the customer the tenant already pays as; null makes a new customer from <c>Email</c> and
/// <c>Name</c>, never one the provider finds by that email, so each tenant has its own customer and its
/// portal shows nothing of another tenant's.
/// </summary>
public sealed record BillingCheckout(string ProductId, Guid TenantRef, string Email, string? Name, string? ReturnUrl, string? CustomerId = null);

/// <summary>
/// A subscription as the provider reports it. <c>TenantRef</c> is the tenant its checkout was for, from
/// the metadata; <c>PeriodEnd</c> is the next billing date, the end of what has been paid for;
/// <c>Scheduled</c> is a change the provider will make then.
/// </summary>
public sealed record ProviderSubscription(
    string Id,
    SubscriptionStatus Status,
    string ProductId,
    string CustomerId,
    Guid? TenantRef,
    DateTimeOffset PeriodEnd,
    bool CancelAtPeriodEnd,
    ProviderScheduledChange? Scheduled = null);

/// <summary>A plan change the provider will make at <c>EffectiveAt</c>.</summary>
public sealed record ProviderScheduledChange(string ProductId, DateTimeOffset EffectiveAt);

/// <summary>When a plan change takes effect.</summary>
public enum PlanChangeTiming
{
    /// <summary>At once, with the difference for the rest of the period charged now.</summary>
    Now,

    /// <summary>At the end of the period paid for, so what was paid for is kept.</summary>
    AtPeriodEnd
}

/// <summary>
/// A payment as the provider reports it. <c>SubscriptionId</c> is the subscription it paid for, null for
/// a one-time purchase; <c>Lines</c> is what it bought; <c>FullyRefunded</c> is true once all of it has
/// been refunded; <c>Dispute</c> is where its latest chargeback stands.
/// </summary>
public sealed record ProviderPayment(
    string Id,
    bool Succeeded,
    string CustomerId,
    Guid? TenantRef,
    string? SubscriptionId,
    IReadOnlyList<PaymentLine> Lines,
    bool FullyRefunded,
    DateTimeOffset CreatedAt,
    PaymentDispute Dispute = PaymentDispute.None)
{
    /// <summary>The money went back to the buyer: refunded in full, or a chargeback lost.</summary>
    public bool Reversed => FullyRefunded || Dispute == PaymentDispute.Lost;
}

/// <summary>Where a payment's latest chargeback stands.</summary>
public enum PaymentDispute
{
    /// <summary>None, or one won or withdrawn: the payment stands.</summary>
    None,

    /// <summary>Opened or challenged, not decided yet.</summary>
    Open,

    /// <summary>Lost, accepted or expired: the money went back to the buyer.</summary>
    Lost
}

public sealed record PaymentLine(string ProductId, int Quantity);

/// <summary>
/// Ids the provider listed, each once. <c>Complete</c> is false when the list was cut short at the most
/// pages read at once, so the rest were not.
/// </summary>
public sealed record ProviderIdList(IReadOnlyCollection<string> Ids, bool Complete);

/// <summary>A verified webhook: its id (for idempotency), its type, and what it names.</summary>
public sealed record ProviderWebhook(string EventId, string Type, string? SubscriptionId, string? PaymentId);

/// <summary>
/// A product as the provider prices it: the amount in the currency's main unit (before tax), the currency,
/// for a subscription how often it charges (<c>Interval</c> null when it is neither monthly nor yearly),
/// and whether it takes less than that amount at first: a discount, or a free trial.
/// </summary>
public sealed record ProviderProduct(decimal Amount, string Currency, bool Recurring, BillingInterval? Interval, bool Discounted = false, int TrialDays = 0);

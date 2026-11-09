using Wbskt.Auth.Host.Models;
using Wbskt.Infrastructure.Events.Outbox;

namespace Wbskt.Auth.Host.Providers;

/// <summary>What the auth database keeps of payments: webhooks received, subscriptions and credit packs bought.</summary>
internal interface ITenantBillingProvider
{
    /// <summary>Stores a delivered webhook once per provider and event id, and counts the delivery.</summary>
    Task<BillingWebhookDelivery> RecordWebhookAsync(string provider, string eventId, string eventType, string payload, CancellationToken cancellationToken);

    /// <summary>Marks a stored webhook done (<paramref name="processed"/>), or records why it failed.</summary>
    Task CompleteWebhookAsync(long id, bool processed, string? error, CancellationToken cancellationToken);

    Task<TenantSubscription?> FindSubscriptionAsync(int tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the tenant's subscription as just read from the provider, replacing any other, and returns
    /// it as stored. <paramref name="refundedThrough"/> sets its refund mark; null keeps the stored one.
    /// </summary>
    Task<TenantSubscription> UpsertSubscriptionAsync(TenantSubscription subscription, DateTime now, DateTime? refundedThrough, CancellationToken cancellationToken);

    /// <summary>Tenants on a paid plan whose subscription may have lapsed with no webhook to say so.</summary>
    Task<IReadOnlyCollection<int>> GetSubscriptionsToReviewAsync(DateTime now, DateTime pastDueSince, CancellationToken cancellationToken);

    /// <summary>Records a bought credit pack and queues its grant, once per payment. False when it was already recorded.</summary>
    Task<bool> RecordCreditPurchaseAsync(string provider, string paymentId, int tenantId, string customerId, decimal credits, DateTime purchasedAt, DateTime expiresAt, OutboxMessage granted, CancellationToken cancellationToken);

    /// <summary>Marks a credit pack refunded and queues its revocation, once. False when there was nothing to refund.</summary>
    Task<bool> RefundCreditPurchaseAsync(string provider, string paymentId, DateTime refundedAt, OutboxMessage revoked, CancellationToken cancellationToken);

    Task<TenantCreditPurchase?> FindCreditPurchaseAsync(string provider, string paymentId, CancellationToken cancellationToken);

    /// <summary>The tenant's customer id with <paramref name="provider"/>, for its portal; null when it never paid.</summary>
    Task<string?> FindCustomerIdAsync(int tenantId, string provider, CancellationToken cancellationToken);
}

using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure.Events.Outbox;

namespace Wbskt.Auth.Host.Providers;

/// <summary>What the auth database keeps of payments: webhooks received, subscriptions and credit packs bought.</summary>
internal interface ITenantBillingProvider
{
    /// <summary>Stores a delivered webhook once per provider and event id, and counts the delivery.</summary>
    Task<BillingWebhookDelivery> RecordWebhookAsync(string provider, string eventId, string eventType, string payload, CancellationToken cancellationToken);

    /// <summary>Marks a stored webhook done (<paramref name="processed"/>), or records why it failed.</summary>
    Task CompleteWebhookAsync(long id, bool processed, string? error, CancellationToken cancellationToken);

    /// <summary>Every subscription the tenant has had, oldest first; <see cref="Services.Billing.SubscriptionPlan.Deciding"/> picks the one that sets its plan.</summary>
    Task<IReadOnlyCollection<TenantSubscription>> GetSubscriptionsAsync(int tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Writes a subscription as just read from the provider and returns it as stored.
    /// <paramref name="refundedThrough"/> sets its refund mark; null keeps the stored one.
    /// </summary>
    Task<TenantSubscription> UpsertSubscriptionAsync(TenantSubscription subscription, DateTime now, DateTime? refundedThrough, CancellationToken cancellationToken);

    /// <summary>Marks a subscription paid for beside an older one, before it is cancelled. False when it was already marked.</summary>
    Task<bool> MarkDuplicateSubscriptionAsync(string provider, string subscriptionId, DateTime now, CancellationToken cancellationToken);

    /// <summary>
    /// Claims the refund of a duplicate subscription's payment so it is asked for once, or gives the claim
    /// back (<paramref name="release"/>) when the provider refused it. False when nothing changed.
    /// </summary>
    Task<bool> ClaimDuplicateRefundAsync(string provider, string subscriptionId, string paymentId, bool release, CancellationToken cancellationToken);

    /// <summary>The subscriptions that could still charge, of the tenants deleting <paramref name="userId"/>'s account deletes.</summary>
    Task<IReadOnlyCollection<TenantSubscription>> GetLiveSubscriptionsOfSoleTenantsAsync(int userId, CancellationToken cancellationToken);

    /// <summary>
    /// Tenants whose plan may have to change with no webhook to say so: a paid plan whose subscription
    /// lapsed, or an operator's plan past its end date.
    /// </summary>
    Task<IReadOnlyCollection<int>> GetSubscriptionsToReviewAsync(DateTime now, DateTime pastDueSince, CancellationToken cancellationToken);

    /// <summary>Records a bought credit pack and queues its grant, once per payment. False when it was already recorded.</summary>
    Task<bool> RecordCreditPurchaseAsync(string provider, string paymentId, int tenantId, string customerId, decimal credits, DateTime purchasedAt, DateTime expiresAt, OutboxMessage granted, CancellationToken cancellationToken);

    /// <summary>Marks a credit pack refunded and queues its revocation, once. False when there was nothing to refund.</summary>
    Task<bool> RefundCreditPurchaseAsync(string provider, string paymentId, DateTime refundedAt, OutboxMessage revoked, CancellationToken cancellationToken);

    /// <summary>Credit packs bought, in id order after <paramref name="afterId"/>, for the main database's reconcile.</summary>
    Task<IReadOnlyCollection<CreditPurchaseResponse>> GetCreditPurchasesAsync(int? afterId, int take, CancellationToken cancellationToken);

    Task<TenantCreditPurchase?> FindCreditPurchaseAsync(string provider, string paymentId, CancellationToken cancellationToken);

    /// <summary>
    /// Holds a credit pack while a chargeback on it is open, or releases it, from <paramref name="expectedRevision"/>,
    /// queuing <paramref name="changed"/> in the same transaction. False when nothing changed.
    /// </summary>
    Task<bool> SetCreditPurchaseHoldAsync(string provider, string paymentId, bool held, DateTime at, int expectedRevision, OutboxMessage changed, CancellationToken cancellationToken);

    /// <summary>The page of the tenant's plan checkout for <paramref name="productId"/>, while it is still open.</summary>
    Task<string?> FindOpenCheckoutAsync(int tenantId, string provider, string productId, DateTime now, CancellationToken cancellationToken);

    /// <summary>Records the plan checkout the tenant just opened, replacing the one before.</summary>
    Task RecordCheckoutAsync(int tenantId, string provider, string productId, string checkoutUrl, DateTime openedAt, DateTime expiresAt, CancellationToken cancellationToken);

    /// <summary>The tenant's customer id with <paramref name="provider"/>, for its portal; null when it never paid.</summary>
    Task<string?> FindCustomerIdAsync(int tenantId, string provider, CancellationToken cancellationToken);

    /// <summary>
    /// Claims the notice <paramref name="kind"/> for <paramref name="periodKey"/> and queues <paramref name="mails"/>
    /// with it, in one transaction: true the first time only, and nothing is queued after.
    /// </summary>
    Task<bool> RecordBillingNoticeAsync(int tenantId, BillingNoticeKind kind, string periodKey, IReadOnlyCollection<OutboundMail> mails, CancellationToken cancellationToken);

    /// <summary>Leases up to <paramref name="take"/> due billing emails until <paramref name="leaseUntil"/>, counting the attempt.</summary>
    Task<IReadOnlyCollection<BillingNoticeMail>> TakeBillingNoticeMailsAsync(DateTime now, DateTime leaseUntil, int take, CancellationToken cancellationToken);

    /// <summary>Records a leased billing email as sent, to retry at <paramref name="retryAt"/>, or given up.</summary>
    Task CompleteBillingNoticeMailAsync(long id, DateTime? sentAt, DateTime? retryAt, DateTime? gaveUpAt, string? error, CancellationToken cancellationToken);

    /// <summary>
    /// Hands back a mail leased until <paramref name="leaseUntil"/> that was never tried, due again at
    /// <paramref name="now"/> with its attempt returned (<c>dbo.BillingNoticeMail_Release</c>).
    /// </summary>
    Task ReleaseBillingNoticeMailAsync(long id, DateTime leaseUntil, DateTime now, CancellationToken cancellationToken);

    /// <summary>Removes billing emails sent or given up before <paramref name="before"/>.</summary>
    Task PurgeBillingNoticeMailsAsync(DateTime before, CancellationToken cancellationToken);

    /// <summary>The tenant's members who can be written to; which of them manage billing is the caller's to check.</summary>
    Task<IReadOnlyCollection<BillingContact>> GetBillingContactsAsync(int tenantId, CancellationToken cancellationToken);

    /// <summary>Subscriptions still behind on payment that fell behind after <paramref name="from"/> and by <paramref name="to"/>.</summary>
    Task<IReadOnlyCollection<PastDueSubscription>> GetPastDueBetweenAsync(DateTime from, DateTime to, CancellationToken cancellationToken);
}

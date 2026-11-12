using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services.Billing;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Events.Outbox;
using Wbskt.Models;
using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Providers;

internal sealed class TenantBillingProvider(IConfiguration configuration) : BaseSqlProvider(configuration, "AuthDBConnection"), ITenantBillingProvider
{
    public Task<BillingWebhookDelivery> RecordWebhookAsync(string provider, string eventId, string eventType, string payload, CancellationToken cancellationToken)
    {
        return ExecuteSingleAsync("dbo.BillingWebhookEvent_Record", p =>
        {
            p.AddWithValue("@Provider", provider);
            p.AddWithValue("@EventId", eventId);
            p.AddWithValue("@EventType", eventType);
            p.AddWithValue("@Payload", payload);
        }, r =>
        {
            int processedAt = r.GetOrdinal("ProcessedAt");
            return new BillingWebhookDelivery(r.GetInt64(r.GetOrdinal("Id")), r.IsDBNull(processedAt) ? null : r.GetDateTime(processedAt));
        }, new InvalidOperationException("BillingWebhookEvent_Record did not return a row."), cancellationToken);
    }

    public Task CompleteWebhookAsync(long id, bool processed, string? error, CancellationToken cancellationToken)
    {
        return ExecuteNonQueryAsync("dbo.BillingWebhookEvent_Complete", p =>
        {
            p.AddWithValue("@Id", id);
            p.AddWithValue("@Processed", processed);
            p.AddWithValue("@Error", error is null ? DBNull.Value : error.Length > 1000 ? error[..1000] : error);
        }, cancellationToken);
    }

    public Task<IReadOnlyCollection<TenantSubscription>> GetSubscriptionsAsync(int tenantId, CancellationToken cancellationToken)
    {
        return ExecuteCollectionAsync("dbo.TenantSubscription_GetBy_TenantId", p => p.AddWithValue("@TenantId", tenantId), MapSubscription, cancellationToken);
    }

    public Task<TenantSubscription> UpsertSubscriptionAsync(TenantSubscription s, DateTime now, DateTime? refundedThrough, CancellationToken cancellationToken)
    {
        return ExecuteSingleAsync("dbo.TenantSubscription_Upsert", p =>
        {
            p.AddWithValue("@TenantId", s.TenantId);
            p.AddWithValue("@Provider", s.Provider);
            p.AddWithValue("@SubscriptionId", s.SubscriptionId);
            p.AddWithValue("@CustomerId", s.CustomerId);
            p.AddWithValue("@ProductId", s.ProductId);
            p.AddWithValue("@PlanCode", s.PlanCode.ToString());
            p.AddWithValue("@BillingInterval", s.Interval.ToString());
            p.AddWithValue("@Status", SubscriptionStatusNames.ToName(s.Status));
            p.AddWithValue("@CurrentPeriodEnd", s.CurrentPeriodEnd);
            p.AddWithValue("@CancelAtPeriodEnd", s.CancelAtPeriodEnd);
            p.AddWithValue("@Now", now);
            p.AddWithValue("@RefundedThrough", (object?)refundedThrough ?? DBNull.Value);
            p.AddWithValue("@ScheduledPlanCode", (object?)s.ScheduledPlan?.ToString() ?? DBNull.Value);
            p.AddWithValue("@ScheduledBillingInterval", (object?)s.ScheduledInterval?.ToString() ?? DBNull.Value);
            p.AddWithValue("@ScheduledAt", (object?)s.ScheduledAt ?? DBNull.Value);
        }, MapSubscription, new InvalidOperationException("TenantSubscription_Upsert did not return a row."), cancellationToken);
    }

    public async Task<bool> MarkDuplicateSubscriptionAsync(string provider, string subscriptionId, DateTime now, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>("dbo.TenantSubscription_MarkDuplicate", p =>
        {
            p.AddWithValue("@Provider", provider);
            p.AddWithValue("@SubscriptionId", subscriptionId);
            p.Add("@Now", SqlDbType.DateTime2).Value = now;
        }, cancellationToken) == 1;
    }

    public async Task<bool> ClaimDuplicateRefundAsync(string provider, string subscriptionId, string paymentId, bool release, DateTime now, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>("dbo.TenantSubscription_ClaimDuplicateRefund", p =>
        {
            p.AddWithValue("@Provider", provider);
            p.AddWithValue("@SubscriptionId", subscriptionId);
            p.AddWithValue("@PaymentId", paymentId);
            p.AddWithValue("@Release", release);
            p.Add("@Now", SqlDbType.DateTime2).Value = now;
        }, cancellationToken) == 1;
    }

    public Task<IReadOnlyCollection<TenantSubscription>> GetLiveSubscriptionsOfSoleTenantsAsync(int userId, CancellationToken cancellationToken)
    {
        return ExecuteCollectionAsync("dbo.TenantSubscription_GetLiveBy_SoleMember", p => p.AddWithValue("@UserId", userId), MapSubscription, cancellationToken);
    }

    public Task<IReadOnlyCollection<int>> GetSubscriptionsToReviewAsync(DateTime now, DateTime pastDueSince, CancellationToken cancellationToken)
    {
        return ExecuteCollectionAsync("dbo.TenantSubscriptions_GetToReview", p =>
        {
            p.AddWithValue("@Now", now);
            p.AddWithValue("@PastDueSince", pastDueSince);
        }, r => r.GetInt32(r.GetOrdinal("TenantId")), cancellationToken);
    }

    public async Task<bool> RecordCreditPurchaseAsync(string provider, string paymentId, int tenantId, string customerId, decimal credits, DateTime purchasedAt, DateTime expiresAt, OutboxMessage granted, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>("dbo.TenantCreditPurchase_Record", p =>
        {
            p.AddWithValue("@Provider", provider);
            p.AddWithValue("@PaymentId", paymentId);
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@CustomerId", customerId);
            p.AddWithValue("@Credits", credits);
            p.AddWithValue("@PurchasedAt", purchasedAt);
            p.AddWithValue("@ExpiresAt", expiresAt);
            p.AddWithValue("@OutboxMessageType", granted.MessageType);
            p.AddWithValue("@OutboxPayload", granted.Payload);
        }, cancellationToken) == 1;
    }

    public async Task<bool> RefundCreditPurchaseAsync(string provider, string paymentId, DateTime refundedAt, OutboxMessage revoked, CancellationToken cancellationToken)
    {
        int? tenantId = await ExecuteScalarAsync<int?>("dbo.TenantCreditPurchase_Refund", p =>
        {
            p.AddWithValue("@Provider", provider);
            p.AddWithValue("@PaymentId", paymentId);
            p.AddWithValue("@RefundedAt", refundedAt);
            p.AddWithValue("@OutboxMessageType", revoked.MessageType);
            p.AddWithValue("@OutboxPayload", revoked.Payload);
        }, cancellationToken);
        return tenantId is not null;
    }

    public Task<IReadOnlyCollection<CreditPurchaseResponse>> GetCreditPurchasesAsync(int? afterId, int take, CancellationToken cancellationToken)
    {
        return ExecuteCollectionAsync("dbo.TenantCreditPurchase_GetPage", p =>
        {
            p.AddWithValue("@AfterId", (object?)afterId ?? DBNull.Value);
            p.AddWithValue("@Take", take);
        }, r =>
        {
            int refundedAt = r.GetOrdinal("RefundedAt");
            return new CreditPurchaseResponse(
                r.GetInt32(r.GetOrdinal("Id")),
                r.GetInt32(r.GetOrdinal("TenantId")),
                r.GetGuid(r.GetOrdinal("TenantRefId")),
                r.GetString(r.GetOrdinal("Provider")),
                r.GetString(r.GetOrdinal("PaymentId")),
                r.GetDecimal(r.GetOrdinal("Credits")),
                r.GetDateTime(r.GetOrdinal("PurchasedAt")).ToUtcOffset(),
                r.GetDateTime(r.GetOrdinal("ExpiresAt")).ToUtcOffset(),
                r.IsDBNull(refundedAt) ? null : r.GetDateTime(refundedAt).ToUtcOffset(),
                !r.IsDBNull(r.GetOrdinal("HeldAt")),
                r.GetInt32(r.GetOrdinal("HoldRevision")));
        }, cancellationToken);
    }

    public Task<TenantCreditPurchase?> FindCreditPurchaseAsync(string provider, string paymentId, CancellationToken cancellationToken)
    {
        return ExecuteFindAsync("dbo.TenantCreditPurchase_FindBy_PaymentId", p =>
        {
            p.AddWithValue("@Provider", provider);
            p.AddWithValue("@PaymentId", paymentId);
        }, r =>
        {
            int refundedAt = r.GetOrdinal("RefundedAt");
            int heldAt = r.GetOrdinal("HeldAt");
            return new TenantCreditPurchase(
                r.GetInt32(r.GetOrdinal("TenantId")),
                r.GetDecimal(r.GetOrdinal("Credits")),
                r.IsDBNull(refundedAt) ? null : r.GetDateTime(refundedAt),
                r.IsDBNull(heldAt) ? null : r.GetDateTime(heldAt),
                r.GetInt32(r.GetOrdinal("HoldRevision")));
        }, cancellationToken);
    }

    public async Task<bool> SetCreditPurchaseHoldAsync(string provider, string paymentId, bool held, DateTime at, int expectedRevision, OutboxMessage changed, CancellationToken cancellationToken)
    {
        return await ExecuteScalarAsync<int>("dbo.TenantCreditPurchase_SetHold", p =>
        {
            p.AddWithValue("@Provider", provider);
            p.AddWithValue("@PaymentId", paymentId);
            p.AddWithValue("@Held", held);
            p.AddWithValue("@At", at);
            p.AddWithValue("@ExpectedRevision", expectedRevision);
            p.AddWithValue("@OutboxMessageType", changed.MessageType);
            p.AddWithValue("@OutboxPayload", changed.Payload);
        }, cancellationToken) == 1;
    }

    public Task<string?> FindOpenCheckoutAsync(int tenantId, string provider, string productId, DateTime now, CancellationToken cancellationToken)
    {
        return ExecuteScalarAsync<string>("dbo.TenantCheckout_FindOpen", p =>
        {
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@Provider", provider);
            p.AddWithValue("@ProductId", productId);
            p.AddWithValue("@Now", now);
        }, cancellationToken);
    }

    public Task RecordCheckoutAsync(int tenantId, string provider, string productId, string checkoutUrl, DateTime openedAt, DateTime expiresAt, CancellationToken cancellationToken)
    {
        return ExecuteNonQueryAsync("dbo.TenantCheckout_Record", p =>
        {
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@Provider", provider);
            p.AddWithValue("@ProductId", productId);
            p.AddWithValue("@CheckoutUrl", checkoutUrl);
            p.AddWithValue("@OpenedAt", openedAt);
            p.AddWithValue("@ExpiresAt", expiresAt);
        }, cancellationToken);
    }

    public Task<string?> FindCustomerIdAsync(int tenantId, string provider, CancellationToken cancellationToken)
    {
        return ExecuteScalarAsync<string>("dbo.TenantBillingCustomer_FindBy_TenantId", p =>
        {
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@Provider", provider);
        }, cancellationToken);
    }

    public async Task<bool> RecordBillingNoticeAsync(int tenantId, BillingNoticeKind kind, string periodKey, IReadOnlyCollection<OutboundMail> mails, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(mails.Select(m => new { to = m.To, subject = m.Subject, htmlBody = m.HtmlBody }));
        return await ExecuteScalarAsync<int>("dbo.BillingNotice_Record", p =>
        {
            p.AddWithValue("@TenantId", tenantId);
            p.AddWithValue("@Kind", kind.ToString());
            p.AddWithValue("@PeriodKey", periodKey);
            p.AddWithValue("@Mails", json);
        }, cancellationToken) == 1;
    }

    public Task<IReadOnlyCollection<BillingNoticeMail>> TakeBillingNoticeMailsAsync(DateTime now, DateTime leaseUntil, int take, CancellationToken cancellationToken)
    {
        return ExecuteCollectionAsync("dbo.BillingNoticeMails_Take", p =>
        {
            p.AddWithValue("@Now", now);
            p.AddWithValue("@LeaseUntil", leaseUntil);
            p.AddWithValue("@Take", take);
        }, r => new BillingNoticeMail(
            r.GetInt64(r.GetOrdinal("Id")),
            r.GetInt32(r.GetOrdinal("TenantId")),
            Enum.Parse<BillingNoticeKind>(r.GetString(r.GetOrdinal("Kind"))),
            r.GetString(r.GetOrdinal("ToAddress")),
            r.GetString(r.GetOrdinal("Subject")),
            r.GetString(r.GetOrdinal("HtmlBody")),
            r.GetInt32(r.GetOrdinal("Attempts"))), cancellationToken);
    }

    public Task CompleteBillingNoticeMailAsync(long id, DateTime? sentAt, DateTime? retryAt, DateTime? gaveUpAt, string? error, CancellationToken cancellationToken)
    {
        return ExecuteNonQueryAsync("dbo.BillingNoticeMail_Complete", p =>
        {
            p.AddWithValue("@Id", id);
            p.AddWithValue("@SentAt", (object?)sentAt ?? DBNull.Value);
            p.AddWithValue("@RetryAt", (object?)retryAt ?? DBNull.Value);
            p.AddWithValue("@GaveUpAt", (object?)gaveUpAt ?? DBNull.Value);
            p.AddWithValue("@Error", error is null ? DBNull.Value : error.Length > 1000 ? error[..1000] : error);
        }, cancellationToken);
    }

    public Task ReleaseBillingNoticeMailAsync(long id, DateTime leaseUntil, DateTime now, CancellationToken cancellationToken)
    {
        return ExecuteNonQueryAsync("dbo.BillingNoticeMail_Release", p =>
        {
            p.AddWithValue("@Id", id);
            p.AddWithValue("@LeaseUntil", leaseUntil);
            p.AddWithValue("@Now", now);
        }, cancellationToken);
    }

    public Task PurgeBillingNoticeMailsAsync(DateTime before, CancellationToken cancellationToken)
    {
        return ExecuteNonQueryAsync("dbo.BillingNoticeMails_Purge", p => p.AddWithValue("@Before", before), cancellationToken);
    }

    public Task<IReadOnlyCollection<BillingContact>> GetBillingContactsAsync(int tenantId, CancellationToken cancellationToken)
    {
        return ExecuteCollectionAsync("dbo.TenantMember_GetContacts", p => p.AddWithValue("@TenantId", tenantId), r => new BillingContact(
            r.GetInt32(r.GetOrdinal("Id")),
            r.GetString(r.GetOrdinal("Username")),
            r.GetString(r.GetOrdinal("Email"))), cancellationToken);
    }

    public Task<IReadOnlyCollection<PastDueSubscription>> GetPastDueBetweenAsync(DateTime from, DateTime to, CancellationToken cancellationToken)
    {
        return ExecuteCollectionAsync("dbo.TenantSubscriptions_GetPastDueBetween", p =>
        {
            p.AddWithValue("@From", from);
            p.AddWithValue("@To", to);
        }, r => new PastDueSubscription(
            r.GetInt32(r.GetOrdinal("TenantId")),
            r.GetString(r.GetOrdinal("SubscriptionId")),
            Enum.Parse<PlanCode>(r.GetString(r.GetOrdinal("PlanCode"))),
            r.GetDateTime(r.GetOrdinal("PastDueSince"))), cancellationToken);
    }

    private static TenantSubscription MapSubscription(SqlDataReader r)
    {
        int pastDueSince = r.GetOrdinal("PastDueSince");
        int refundedThrough = r.GetOrdinal("RefundedThrough");
        int duplicateAt = r.GetOrdinal("DuplicateAt");
        int duplicateRefund = r.GetOrdinal("DuplicateRefundPaymentId");
        int scheduledPlan = r.GetOrdinal("ScheduledPlanCode");
        int scheduledInterval = r.GetOrdinal("ScheduledBillingInterval");
        int scheduledAt = r.GetOrdinal("ScheduledAt");
        SubscriptionStatusNames.TryParse(r.GetString(r.GetOrdinal("Status")), out SubscriptionStatus status);
        return new TenantSubscription(
            r.GetInt32(r.GetOrdinal("TenantId")),
            r.GetString(r.GetOrdinal("Provider")),
            r.GetString(r.GetOrdinal("SubscriptionId")),
            r.GetString(r.GetOrdinal("CustomerId")),
            r.GetString(r.GetOrdinal("ProductId")),
            Enum.Parse<PlanCode>(r.GetString(r.GetOrdinal("PlanCode"))),
            Enum.Parse<BillingInterval>(r.GetString(r.GetOrdinal("BillingInterval"))),
            status,
            r.GetDateTime(r.GetOrdinal("CurrentPeriodEnd")),
            r.GetBoolean(r.GetOrdinal("CancelAtPeriodEnd")),
            r.IsDBNull(pastDueSince) ? null : r.GetDateTime(pastDueSince),
            r.IsDBNull(refundedThrough) ? null : r.GetDateTime(refundedThrough),
            r.IsDBNull(duplicateAt) ? null : r.GetDateTime(duplicateAt),
            r.IsDBNull(duplicateRefund) ? null : r.GetString(duplicateRefund),
            r.GetDateTime(r.GetOrdinal("CreatedAt")),
            r.IsDBNull(scheduledPlan) ? null : Enum.Parse<PlanCode>(r.GetString(scheduledPlan)),
            r.IsDBNull(scheduledInterval) ? null : Enum.Parse<BillingInterval>(r.GetString(scheduledInterval)),
            r.IsDBNull(scheduledAt) ? null : r.GetDateTime(scheduledAt));
    }
}

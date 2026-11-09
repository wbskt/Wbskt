using Microsoft.Data.SqlClient;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services.Billing;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Events.Outbox;
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

    public Task<TenantSubscription?> FindSubscriptionAsync(int tenantId, CancellationToken cancellationToken)
    {
        return ExecuteFindAsync("dbo.TenantSubscription_GetBy_TenantId", p => p.AddWithValue("@TenantId", tenantId), MapSubscription, cancellationToken);
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
        }, MapSubscription, new InvalidOperationException("TenantSubscription_Upsert did not return a row."), cancellationToken);
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

    public Task<TenantCreditPurchase?> FindCreditPurchaseAsync(string provider, string paymentId, CancellationToken cancellationToken)
    {
        return ExecuteFindAsync("dbo.TenantCreditPurchase_FindBy_PaymentId", p =>
        {
            p.AddWithValue("@Provider", provider);
            p.AddWithValue("@PaymentId", paymentId);
        }, r =>
        {
            int refundedAt = r.GetOrdinal("RefundedAt");
            return new TenantCreditPurchase(r.GetInt32(r.GetOrdinal("TenantId")), r.GetDecimal(r.GetOrdinal("Credits")), r.IsDBNull(refundedAt) ? null : r.GetDateTime(refundedAt));
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

    private static TenantSubscription MapSubscription(SqlDataReader r)
    {
        int pastDueSince = r.GetOrdinal("PastDueSince");
        int refundedThrough = r.GetOrdinal("RefundedThrough");
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
            r.IsDBNull(refundedThrough) ? null : r.GetDateTime(refundedThrough));
    }
}

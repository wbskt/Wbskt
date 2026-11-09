using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.AuthDatabase;

/// <summary>
/// The auth database's payment procedures (#631): a webhook stored once and acted on once, a tenant's
/// subscription as last read from the provider, the subscriptions the review worker looks at again, and
/// credit packs recorded and refunded once each with their events.
/// </summary>
[Collection("SqlEdge")]
public sealed class BillingPaymentProcedureTests(AuthSqlFixture fixture)
{
    private const string Skipped = "SQL Server not reachable — skipping.";
    private const string GrantedType = "Wbskt.Events.Billing.TenantCreditsGrantedEvent";
    private const string RevokedType = "Wbskt.Events.Billing.TenantCreditGrantRevokedEvent";
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task A_webhook_is_stored_once_and_a_redelivery_of_one_acted_on_says_so()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        string eventId = $"evt_{Guid.NewGuid():N}";

        var first = await RecordWebhookAsync(eventId);
        await CompleteWebhookAsync(first.Id, processed: false, "HttpRequestException: down");
        var retried = await RecordWebhookAsync(eventId);
        await CompleteWebhookAsync(retried.Id, processed: true, error: null);
        var redelivered = await RecordWebhookAsync(eventId);

        Assert.Null(first.ProcessedAt);
        Assert.Equal(first.Id, retried.Id);
        Assert.Null(retried.ProcessedAt);
        Assert.NotNull(redelivered.ProcessedAt);
        Assert.Equal(3, await ScalarAsync<int>("SELECT Attempts FROM dbo.BillingWebhookEvents WHERE Id = @p0", first.Id));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.BillingWebhookEvents WHERE EventId = @p0", eventId));
    }

    [SkippableFact]
    public async Task Past_due_is_dated_from_when_it_first_fell_behind_and_cleared_once_paid()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = await CreateTenantAsync();
        string subscriptionId = $"sub_{Guid.NewGuid():N}";

        var behind = await UpsertAsync(tenantId, subscriptionId, "past_due", Now);
        var stillBehind = await UpsertAsync(tenantId, subscriptionId, "on_hold", Now.AddDays(2));
        var paid = await UpsertAsync(tenantId, subscriptionId, "active", Now.AddDays(3));

        Assert.Equal(Now, behind.PastDueSince);
        Assert.Equal(Now, stillBehind.PastDueSince);
        Assert.Null(paid.PastDueSince);
        Assert.Equal("active", paid.Status);
    }

    [SkippableFact]
    public async Task A_refund_mark_is_kept_across_reads_and_a_new_subscription_replaces_the_old()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = await CreateTenantAsync();
        string first = $"sub_{Guid.NewGuid():N}";
        string second = $"sub_{Guid.NewGuid():N}";

        await UpsertAsync(tenantId, first, "active", Now, refundedThrough: Now.AddDays(31));
        var reread = await UpsertAsync(tenantId, first, "active", Now.AddHours(1));
        var replaced = await UpsertAsync(tenantId, second, "active", Now.AddDays(1));

        Assert.Equal(Now.AddDays(31), reread.RefundedThrough);
        Assert.Equal(second, replaced.SubscriptionId);
        Assert.Null(replaced.RefundedThrough);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.TenantSubscriptions WHERE TenantId = @p0", tenantId));
    }

    [SkippableFact]
    public async Task Review_lists_paid_tenants_whose_subscription_lapsed_with_no_webhook()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int overdue = await CreateTenantAsync("Maker");
        int withinGrace = await CreateTenantAsync("Maker");
        int cancelledEnded = await CreateTenantAsync("Pro");
        int cancelledRunning = await CreateTenantAsync("Pro");
        int alreadyFree = await CreateTenantAsync();
        await UpsertAsync(overdue, $"sub_{Guid.NewGuid():N}", "past_due", Now.AddDays(-8), plan: "Maker");
        await UpsertAsync(withinGrace, $"sub_{Guid.NewGuid():N}", "past_due", Now.AddDays(-2), plan: "Maker");
        await UpsertAsync(cancelledEnded, $"sub_{Guid.NewGuid():N}", "cancelled", Now.AddDays(-40), periodEnd: Now.AddDays(-1));
        await UpsertAsync(cancelledRunning, $"sub_{Guid.NewGuid():N}", "cancelled", Now.AddDays(-5), periodEnd: Now.AddDays(10));
        await UpsertAsync(alreadyFree, $"sub_{Guid.NewGuid():N}", "expired", Now.AddDays(-5), plan: "Maker");

        IReadOnlyCollection<int> listed = await GetToReviewAsync(Now, pastDueSince: Now.AddDays(-7));

        Assert.Contains(overdue, listed);
        Assert.Contains(cancelledEnded, listed);
        Assert.DoesNotContain(withinGrace, listed);
        Assert.DoesNotContain(cancelledRunning, listed);
        Assert.DoesNotContain(alreadyFree, listed);
    }

    [SkippableFact]
    public async Task A_credit_pack_is_recorded_and_refunded_once_each_with_its_event()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = await CreateTenantAsync();
        string paymentId = $"pay_{Guid.NewGuid():N}";
        string granted = $"{{\"marker\":\"{Guid.NewGuid():N}\"}}";
        string revoked = $"{{\"marker\":\"{Guid.NewGuid():N}\"}}";

        int firstRecord = await RecordPurchaseAsync(tenantId, paymentId, granted);
        int secondRecord = await RecordPurchaseAsync(tenantId, paymentId, granted);
        int? firstRefund = await RefundPurchaseAsync(paymentId, revoked);
        int? secondRefund = await RefundPurchaseAsync(paymentId, revoked);

        Assert.Equal((1, 0), (firstRecord, secondRecord));
        Assert.Equal(tenantId, firstRefund);
        Assert.Null(secondRefund);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.OutboxMessages WHERE MessageType = @p0 AND Payload = @p1", GrantedType, granted));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.OutboxMessages WHERE MessageType = @p0 AND Payload = @p1", RevokedType, revoked));
        Assert.NotNull(await ScalarAsync<DateTime?>("SELECT RefundedAt FROM dbo.TenantCreditPurchases WHERE PaymentId = @p0", paymentId));
    }

    [SkippableFact]
    public async Task The_portal_customer_is_the_subscriptions_before_a_credit_packs()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = await CreateTenantAsync();
        await RecordPurchaseAsync(tenantId, $"pay_{Guid.NewGuid():N}", "{}", customerId: "cus_pack");

        string? beforeSubscribing = await FindCustomerAsync(tenantId);
        await UpsertAsync(tenantId, $"sub_{Guid.NewGuid():N}", "active", Now, customerId: "cus_sub");
        string? afterSubscribing = await FindCustomerAsync(tenantId);

        Assert.Equal("cus_pack", beforeSubscribing);
        Assert.Equal("cus_sub", afterSubscribing);
        Assert.Null(await FindCustomerAsync(await CreateTenantAsync()));
    }

    private async Task<(long Id, DateTime? ProcessedAt)> RecordWebhookAsync(string eventId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = Procedure(conn, "dbo.BillingWebhookEvent_Record");
        cmd.Parameters.AddWithValue("@Provider", "dodo");
        cmd.Parameters.AddWithValue("@EventId", eventId);
        cmd.Parameters.AddWithValue("@EventType", "subscription.active");
        cmd.Parameters.AddWithValue("@Payload", "{}");
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        int processedAt = reader.GetOrdinal("ProcessedAt");
        return (reader.GetInt64(reader.GetOrdinal("Id")), reader.IsDBNull(processedAt) ? null : reader.GetDateTime(processedAt));
    }

    private async Task CompleteWebhookAsync(long id, bool processed, string? error)
    {
        await using var conn = await OpenAsync();
        await using var cmd = Procedure(conn, "dbo.BillingWebhookEvent_Complete");
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@Processed", processed);
        cmd.Parameters.AddWithValue("@Error", (object?)error ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<(string SubscriptionId, string Status, DateTime? PastDueSince, DateTime? RefundedThrough)> UpsertAsync(
        int tenantId, string subscriptionId, string status, DateTime now,
        DateTime? refundedThrough = null, string plan = "Pro", DateTime? periodEnd = null, string customerId = "cus_1")
    {
        await using var conn = await OpenAsync();
        await using var cmd = Procedure(conn, "dbo.TenantSubscription_Upsert");
        cmd.Parameters.AddWithValue("@TenantId", tenantId);
        cmd.Parameters.AddWithValue("@Provider", "dodo");
        cmd.Parameters.AddWithValue("@SubscriptionId", subscriptionId);
        cmd.Parameters.AddWithValue("@CustomerId", customerId);
        cmd.Parameters.AddWithValue("@ProductId", "prod_1");
        cmd.Parameters.AddWithValue("@PlanCode", plan);
        cmd.Parameters.AddWithValue("@BillingInterval", "Monthly");
        cmd.Parameters.AddWithValue("@Status", status);
        cmd.Parameters.AddWithValue("@CurrentPeriodEnd", periodEnd ?? now.AddMonths(1));
        cmd.Parameters.AddWithValue("@CancelAtPeriodEnd", status == "cancelled");
        cmd.Parameters.AddWithValue("@Now", now);
        cmd.Parameters.AddWithValue("@RefundedThrough", (object?)refundedThrough ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        int pastDue = reader.GetOrdinal("PastDueSince");
        int refunded = reader.GetOrdinal("RefundedThrough");
        return (reader.GetString(reader.GetOrdinal("SubscriptionId")), reader.GetString(reader.GetOrdinal("Status")),
            reader.IsDBNull(pastDue) ? null : reader.GetDateTime(pastDue), reader.IsDBNull(refunded) ? null : reader.GetDateTime(refunded));
    }

    private async Task<IReadOnlyCollection<int>> GetToReviewAsync(DateTime now, DateTime pastDueSince)
    {
        var tenants = new List<int>();
        await using var conn = await OpenAsync();
        await using var cmd = Procedure(conn, "dbo.TenantSubscriptions_GetToReview");
        cmd.Parameters.AddWithValue("@Now", now);
        cmd.Parameters.AddWithValue("@PastDueSince", pastDueSince);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tenants.Add(reader.GetInt32(0));
        }

        return tenants;
    }

    private async Task<int> RecordPurchaseAsync(int tenantId, string paymentId, string payload, string customerId = "cus_1")
    {
        await using var conn = await OpenAsync();
        await using var cmd = Procedure(conn, "dbo.TenantCreditPurchase_Record");
        cmd.Parameters.AddWithValue("@Provider", "dodo");
        cmd.Parameters.AddWithValue("@PaymentId", paymentId);
        cmd.Parameters.AddWithValue("@TenantId", tenantId);
        cmd.Parameters.AddWithValue("@CustomerId", customerId);
        cmd.Parameters.AddWithValue("@Credits", 500m);
        cmd.Parameters.AddWithValue("@PurchasedAt", Now);
        cmd.Parameters.AddWithValue("@ExpiresAt", Now.AddDays(365));
        cmd.Parameters.AddWithValue("@OutboxMessageType", GrantedType);
        cmd.Parameters.AddWithValue("@OutboxPayload", payload);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<int?> RefundPurchaseAsync(string paymentId, string payload)
    {
        await using var conn = await OpenAsync();
        await using var cmd = Procedure(conn, "dbo.TenantCreditPurchase_Refund");
        cmd.Parameters.AddWithValue("@Provider", "dodo");
        cmd.Parameters.AddWithValue("@PaymentId", paymentId);
        cmd.Parameters.AddWithValue("@RefundedAt", Now);
        cmd.Parameters.AddWithValue("@OutboxMessageType", RevokedType);
        cmd.Parameters.AddWithValue("@OutboxPayload", payload);
        object? value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? null : (int)value;
    }

    private async Task<string?> FindCustomerAsync(int tenantId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = Procedure(conn, "dbo.TenantBillingCustomer_FindBy_TenantId");
        cmd.Parameters.AddWithValue("@TenantId", tenantId);
        cmd.Parameters.AddWithValue("@Provider", "dodo");
        object? value = await cmd.ExecuteScalarAsync();
        return value as string;
    }

    private async Task<int> CreateTenantAsync(string plan = "Free")
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("INSERT INTO dbo.Tenants (RefId, Name, PlanCode) OUTPUT INSERTED.Id VALUES (NEWID(), @Name, @Plan);", conn);
        cmd.Parameters.AddWithValue("@Name", $"pay-{Guid.NewGuid():N}"[..30]);
        cmd.Parameters.AddWithValue("@Plan", plan);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private static SqlCommand Procedure(SqlConnection conn, string name) => new(name, conn) { CommandType = CommandType.StoredProcedure };

    private async Task<T> ScalarAsync<T>(string sql, params object[] args)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < args.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        }

        object? value = await cmd.ExecuteScalarAsync();
        return value is DBNull ? default! : (T)value!;
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }
}

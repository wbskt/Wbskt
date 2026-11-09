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
    private const string HoldType = "Wbskt.Events.Billing.TenantCreditGrantHoldChangedEvent";
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
    public async Task A_scheduled_downgrade_is_stored_as_read_and_cleared_when_the_provider_drops_it()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = await CreateTenantAsync();
        string subscriptionId = $"sub_{Guid.NewGuid():N}";

        var scheduled = await UpsertScheduledAsync(tenantId, subscriptionId, ("Maker", "Yearly", Now.AddMonths(1)));
        var dropped = await UpsertScheduledAsync(tenantId, subscriptionId, null);

        Assert.Equal(("Maker", "Yearly", Now.AddMonths(1)), scheduled);
        Assert.Null(dropped);
    }

    [SkippableFact]
    public async Task A_refund_mark_is_kept_across_reads_and_a_second_subscription_is_kept_beside_the_first()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = await CreateTenantAsync();
        string first = $"sub_{Guid.NewGuid():N}";
        string second = $"sub_{Guid.NewGuid():N}";

        await UpsertAsync(tenantId, first, "active", Now, refundedThrough: Now.AddDays(31));
        var reread = await UpsertAsync(tenantId, first, "active", Now.AddHours(1));
        var added = await UpsertAsync(tenantId, second, "active", Now.AddDays(1));

        Assert.Equal(Now.AddDays(31), reread.RefundedThrough);
        Assert.Equal(second, added.SubscriptionId);
        Assert.Null(added.RefundedThrough);
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.TenantSubscriptions WHERE TenantId = @p0", tenantId));
    }

    [SkippableFact]
    public async Task A_duplicate_is_marked_once_and_its_refund_claimed_once_until_released()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = await CreateTenantAsync();
        string kept = $"sub_{Guid.NewGuid():N}";
        string duplicate = $"sub_{Guid.NewGuid():N}";
        await UpsertAsync(tenantId, kept, "active", Now);
        await UpsertAsync(tenantId, duplicate, "active", Now.AddMinutes(1));

        int claimedUnmarked = await ProcScalarAsync("dbo.TenantSubscription_ClaimDuplicateRefund", ("@Provider", "dodo"), ("@SubscriptionId", duplicate), ("@PaymentId", "pay_1"));
        int marked = await ProcScalarAsync("dbo.TenantSubscription_MarkDuplicate", ("@Provider", "dodo"), ("@SubscriptionId", duplicate), ("@Now", Now));
        int markedAgain = await ProcScalarAsync("dbo.TenantSubscription_MarkDuplicate", ("@Provider", "dodo"), ("@SubscriptionId", duplicate), ("@Now", Now.AddHours(1)));
        int claimed = await ProcScalarAsync("dbo.TenantSubscription_ClaimDuplicateRefund", ("@Provider", "dodo"), ("@SubscriptionId", duplicate), ("@PaymentId", "pay_1"));
        int claimedAgain = await ProcScalarAsync("dbo.TenantSubscription_ClaimDuplicateRefund", ("@Provider", "dodo"), ("@SubscriptionId", duplicate), ("@PaymentId", "pay_1"));
        int released = await ProcScalarAsync("dbo.TenantSubscription_ClaimDuplicateRefund", ("@Provider", "dodo"), ("@SubscriptionId", duplicate), ("@PaymentId", "pay_1"), ("@Release", true));
        int reclaimed = await ProcScalarAsync("dbo.TenantSubscription_ClaimDuplicateRefund", ("@Provider", "dodo"), ("@SubscriptionId", duplicate), ("@PaymentId", "pay_1"));
        await UpsertAsync(tenantId, duplicate, "cancelled", Now.AddHours(2));

        Assert.Equal((0, 1, 0), (claimedUnmarked, marked, markedAgain));
        Assert.Equal((1, 0, 1, 1), (claimed, claimedAgain, released, reclaimed));
        Assert.Equal(Now, await ScalarAsync<DateTime>("SELECT DuplicateAt FROM dbo.TenantSubscriptions WHERE SubscriptionId = @p0", duplicate));
        Assert.Equal("pay_1", await ScalarAsync<string>("SELECT DuplicateRefundPaymentId FROM dbo.TenantSubscriptions WHERE SubscriptionId = @p0", duplicate));
        Assert.Null(await ScalarAsync<DateTime?>("SELECT DuplicateAt FROM dbo.TenantSubscriptions WHERE SubscriptionId = @p0", kept));
    }

    [SkippableFact]
    public async Task Deleting_an_account_cancels_only_the_live_subscriptions_of_tenants_it_is_alone_in()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();
        int otherId = await CreateUserAsync();
        int alone = await CreateTenantAsync("Maker", userId);
        int shared = await CreateTenantAsync("Pro", userId, otherId);
        string live = $"sub_{Guid.NewGuid():N}";
        await UpsertAsync(alone, live, "active", Now);
        await UpsertAsync(alone, $"sub_{Guid.NewGuid():N}", "expired", Now);
        await UpsertAsync(shared, $"sub_{Guid.NewGuid():N}", "active", Now);

        var listed = new List<string>();
        await using (var conn = await OpenAsync())
        await using (var cmd = Procedure(conn, "dbo.TenantSubscription_GetLiveBy_SoleMember"))
        {
            cmd.Parameters.AddWithValue("@UserId", userId);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                listed.Add(reader.GetString(reader.GetOrdinal("SubscriptionId")));
            }
        }

        Assert.Equal([live], listed);
    }

    [SkippableFact]
    public async Task A_pack_hold_changes_only_from_the_revision_read_and_never_on_a_refunded_pack()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = await CreateTenantAsync();
        string paymentId = $"pay_{Guid.NewGuid():N}";
        string marker = Guid.NewGuid().ToString("N");
        await RecordPurchaseAsync(tenantId, paymentId, "{}");

        int held = await SetHoldAsync(paymentId, held: true, expectedRevision: 0, marker);
        int stale = await SetHoldAsync(paymentId, held: false, expectedRevision: 0, marker);
        int heldAgain = await SetHoldAsync(paymentId, held: true, expectedRevision: 1, marker);
        int releasedHold = await SetHoldAsync(paymentId, held: false, expectedRevision: 1, marker);
        await RefundPurchaseAsync(paymentId, "{}");
        int afterRefund = await SetHoldAsync(paymentId, held: true, expectedRevision: 2, marker);

        Assert.Equal((1, 0, 0, 1, 0), (held, stale, heldAgain, releasedHold, afterRefund));
        Assert.Equal(2, await ScalarAsync<int>("SELECT HoldRevision FROM dbo.TenantCreditPurchases WHERE PaymentId = @p0", paymentId));
        Assert.Null(await ScalarAsync<DateTime?>("SELECT HeldAt FROM dbo.TenantCreditPurchases WHERE PaymentId = @p0", paymentId));
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.OutboxMessages WHERE MessageType = @p0 AND Payload LIKE @p1", HoldType, $"%{marker}%"));
    }

    [SkippableFact]
    public async Task An_open_checkout_is_found_until_it_expires_and_the_next_one_replaces_it()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = await CreateTenantAsync();

        await RecordCheckoutAsync(tenantId, "prod_maker_m", "https://pay.test/1");
        string? open = await FindCheckoutAsync(tenantId, "prod_maker_m", Now.AddMinutes(10));
        string? otherProduct = await FindCheckoutAsync(tenantId, "prod_pro_m", Now.AddMinutes(10));
        string? expired = await FindCheckoutAsync(tenantId, "prod_maker_m", Now.AddMinutes(31));
        await RecordCheckoutAsync(tenantId, "prod_pro_m", "https://pay.test/2");
        string? replaced = await FindCheckoutAsync(tenantId, "prod_maker_m", Now.AddMinutes(10));
        string? next = await FindCheckoutAsync(tenantId, "prod_pro_m", Now.AddMinutes(10));

        Assert.Equal("https://pay.test/1", open);
        Assert.Null(otherProduct);
        Assert.Null(expired);
        Assert.Null(replaced);
        Assert.Equal("https://pay.test/2", next);
    }

    [SkippableFact]
    public async Task An_operator_plan_is_recorded_with_its_source_and_end_and_reviewed_once_it_ends()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int ended = await CreateTenantAsync();
        int running = await CreateTenantAsync();
        int noEnd = await CreateTenantAsync();
        await SetPlanAsync(ended, "Business", "Operator", Now.AddDays(-1));
        await SetPlanAsync(running, "Business", "Operator", Now.AddDays(30));
        await SetPlanAsync(noEnd, "Business", "Operator", endsAt: null);

        IReadOnlyCollection<int> listed = await GetToReviewAsync(Now, pastDueSince: Now.AddDays(-7));

        Assert.Equal("Operator", await ScalarAsync<string>("SELECT PlanSource FROM dbo.Tenants WHERE Id = @p0", running));
        Assert.Equal(Now.AddDays(30), await ScalarAsync<DateTime>("SELECT PlanEndsAt FROM dbo.Tenants WHERE Id = @p0", running));
        Assert.Contains(ended, listed);
        Assert.DoesNotContain(running, listed);
        Assert.DoesNotContain(noEnd, listed);
    }

    [SkippableFact]
    public async Task Review_skips_a_plan_an_operator_set_over_a_lapsed_subscription()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = await CreateTenantAsync();
        await SetPlanAsync(tenantId, "Pro", "Subscription", endsAt: null);
        await UpsertAsync(tenantId, $"sub_{Guid.NewGuid():N}", "expired", Now.AddDays(-5));

        bool listedFromSubscription = (await GetToReviewAsync(Now, Now.AddDays(-7))).Contains(tenantId);
        await SetPlanAsync(tenantId, "Pro", "Operator", endsAt: null);
        bool listedFromOperator = (await GetToReviewAsync(Now, Now.AddDays(-7))).Contains(tenantId);

        Assert.True(listedFromSubscription);
        Assert.False(listedFromOperator);
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

    [SkippableFact]
    public async Task Credit_purchases_page_by_id_with_their_refund_and_hold()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = await CreateTenantAsync();
        string refunded = $"pay_{Guid.NewGuid():N}";
        string held = $"pay_{Guid.NewGuid():N}";
        await RecordPurchaseAsync(tenantId, refunded, "{}");
        await RecordPurchaseAsync(tenantId, held, "{}");
        await RefundPurchaseAsync(refunded, "{}");
        await SetHoldAsync(held, held: true, expectedRevision: 0, Guid.NewGuid().ToString("N"));
        int first = await ScalarAsync<int>("SELECT Id FROM dbo.TenantCreditPurchases WHERE PaymentId = @p0", refunded);

        await using var conn = await OpenAsync();
        await using var cmd = Procedure(conn, "dbo.TenantCreditPurchase_GetPage");
        cmd.Parameters.AddWithValue("@AfterId", first - 1);
        cmd.Parameters.AddWithValue("@Take", 2);
        var rows = new List<(string PaymentId, bool Refunded, bool Held, int HoldRevision)>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetString(reader.GetOrdinal("PaymentId")), !reader.IsDBNull(reader.GetOrdinal("RefundedAt")),
                    !reader.IsDBNull(reader.GetOrdinal("HeldAt")), reader.GetInt32(reader.GetOrdinal("HoldRevision"))));
            }
        }

        Assert.Equal([(refunded, true, false, 0), (held, false, true, 1)], rows);
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

    private async Task<(string Plan, string Interval, DateTime At)?> UpsertScheduledAsync(int tenantId, string subscriptionId, (string Plan, string Interval, DateTime At)? scheduled)
    {
        await using var conn = await OpenAsync();
        await using var cmd = Procedure(conn, "dbo.TenantSubscription_Upsert");
        cmd.Parameters.AddWithValue("@TenantId", tenantId);
        cmd.Parameters.AddWithValue("@Provider", "dodo");
        cmd.Parameters.AddWithValue("@SubscriptionId", subscriptionId);
        cmd.Parameters.AddWithValue("@CustomerId", "cus_1");
        cmd.Parameters.AddWithValue("@ProductId", "prod_1");
        cmd.Parameters.AddWithValue("@PlanCode", "Pro");
        cmd.Parameters.AddWithValue("@BillingInterval", "Yearly");
        cmd.Parameters.AddWithValue("@Status", "active");
        cmd.Parameters.AddWithValue("@CurrentPeriodEnd", Now.AddMonths(1));
        cmd.Parameters.AddWithValue("@CancelAtPeriodEnd", false);
        cmd.Parameters.AddWithValue("@Now", Now);
        cmd.Parameters.AddWithValue("@ScheduledPlanCode", (object?)scheduled?.Plan ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ScheduledBillingInterval", (object?)scheduled?.Interval ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ScheduledAt", (object?)scheduled?.At ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        int plan = reader.GetOrdinal("ScheduledPlanCode");
        return reader.IsDBNull(plan)
            ? null
            : (reader.GetString(plan), reader.GetString(reader.GetOrdinal("ScheduledBillingInterval")), reader.GetDateTime(reader.GetOrdinal("ScheduledAt")));
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

    private async Task<int> CreateTenantAsync(string plan = "Free", params int[] members)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("""
            INSERT INTO dbo.Tenants (RefId, Name, PlanCode, PlanSource) OUTPUT INSERTED.Id
            VALUES (NEWID(), @Name, @Plan, CASE WHEN @Plan = N'Free' THEN N'Default' ELSE N'Subscription' END);
            """, conn);
        cmd.Parameters.AddWithValue("@Name", $"pay-{Guid.NewGuid():N}"[..30]);
        cmd.Parameters.AddWithValue("@Plan", plan);
        int tenantId = (int)(await cmd.ExecuteScalarAsync())!;
        foreach (int userId in members)
        {
            await ScalarAsync<int>("INSERT INTO dbo.TenantMembers (TenantId, UserId) VALUES (@p0, @p1); SELECT 1;", tenantId, userId);
        }

        return tenantId;
    }

    private async Task<int> CreateUserAsync()
    {
        string name = $"pay-{Guid.NewGuid():N}"[..30];
        return await ScalarAsync<int>("""
            INSERT INTO dbo.Users (Username, Email, PasswordHash, IsActive) OUTPUT INSERTED.Id
            VALUES (@p0, CONCAT(@p0, '@example.test'), 'hash', 1);
            """, name);
    }

    private async Task SetPlanAsync(int tenantId, string plan, string source, DateTime? endsAt)
    {
        int revision = await ScalarAsync<int>("SELECT PlanRevision FROM dbo.Tenants WHERE Id = @p0", tenantId);
        await using var conn = await OpenAsync();
        await using var cmd = Procedure(conn, "dbo.TenantPlan_Set");
        cmd.Parameters.AddWithValue("@TenantId", tenantId);
        cmd.Parameters.AddWithValue("@PlanCode", plan);
        cmd.Parameters.AddWithValue("@PlanLimitOverrides", DBNull.Value);
        cmd.Parameters.AddWithValue("@ExpectedRevision", revision);
        cmd.Parameters.AddWithValue("@PlanSource", source);
        cmd.Parameters.AddWithValue("@PlanEndsAt", (object?)endsAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@OutboxMessageType", "Wbskt.Events.Auth.TenantPlanChangedEvent");
        cmd.Parameters.AddWithValue("@OutboxPayload", "{}");
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
    }

    private async Task<int> SetHoldAsync(string paymentId, bool held, int expectedRevision, string marker)
    {
        return await ProcScalarAsync("dbo.TenantCreditPurchase_SetHold",
            ("@Provider", "dodo"), ("@PaymentId", paymentId), ("@Held", held), ("@At", Now),
            ("@ExpectedRevision", expectedRevision), ("@OutboxMessageType", HoldType), ("@OutboxPayload", $"{{\"marker\":\"{marker}\"}}"));
    }

    private async Task RecordCheckoutAsync(int tenantId, string productId, string url)
    {
        await ProcScalarAsync("dbo.TenantCheckout_Record",
            ("@TenantId", tenantId), ("@Provider", "dodo"), ("@ProductId", productId), ("@CheckoutUrl", url),
            ("@OpenedAt", Now), ("@ExpiresAt", Now.AddMinutes(30)));
    }

    private async Task<string?> FindCheckoutAsync(int tenantId, string productId, DateTime now)
    {
        await using var conn = await OpenAsync();
        await using var cmd = Procedure(conn, "dbo.TenantCheckout_FindOpen");
        cmd.Parameters.AddWithValue("@TenantId", tenantId);
        cmd.Parameters.AddWithValue("@Provider", "dodo");
        cmd.Parameters.AddWithValue("@ProductId", productId);
        cmd.Parameters.AddWithValue("@Now", now);
        return await cmd.ExecuteScalarAsync() as string;
    }

    private async Task<int> ProcScalarAsync(string name, params (string Name, object Value)[] args)
    {
        await using var conn = await OpenAsync();
        await using var cmd = Procedure(conn, name);
        foreach (var (argName, value) in args)
        {
            cmd.Parameters.AddWithValue(argName, value);
        }

        object? result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
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

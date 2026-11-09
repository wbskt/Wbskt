using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Services.Billing;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Events.Outbox;
using Wbskt.Models.Billing;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// Payments on the auth host (#631): the webhook signature, what a subscription's state puts the tenant
/// on, the sync that acts on what the provider says (duplicates #682, operator plans #685, chargebacks
/// #686), the webhook's once-only handling, checkout's refusals and reuse, and Dodo's JSON.
/// </summary>
public sealed class BillingPaymentsTests
{
    // The example secret published with the Standard Webhooks specification, not a real key.
    private const string Secret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";
    private static readonly Guid TenantRef = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IPaymentGateway> _gateway = new();
    private readonly Mock<ITenantBillingProvider> _billing = new();
    private readonly Mock<ITenantPlanProvider> _plans = new();
    private readonly Mock<ITenantPlanService> _planService = new();
    private readonly Mock<IAuditWorkspaces> _audit = new();
    private readonly Mock<IAccountProvider> _accounts = new();
    private readonly Mock<IBillingNotifier> _notifier = new();
    private readonly List<TenantSubscription> _stored = [];

    private readonly BillingOptions _options = new()
    {
        Plans = new()
        {
            [PlanCode.Maker] = new() { [BillingInterval.Monthly] = "prod_maker_m", [BillingInterval.Yearly] = "prod_maker_y" },
            [PlanCode.Pro] = new() { [BillingInterval.Monthly] = "prod_pro_m" }
        },
        CreditPacks = new() { ["Boost"] = new CreditPackOptions { ProductId = "prod_boost", Credits = 500m } }
    };

    public BillingPaymentsTests()
    {
        _gateway.SetupGet(g => g.Name).Returns("dodo");
        _gateway.SetupGet(g => g.IsConfigured).Returns(true);
        _plans.Setup(p => p.FindByRefIdAsync(TenantRef, It.IsAny<CancellationToken>())).ReturnsAsync(() => Tenant);
        _plans.Setup(p => p.FindByIdAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync(() => Tenant);
        _audit.Setup(a => a.OfTenantAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync([31, 32]);
        _billing.Setup(b => b.UpsertSubscriptionAsync(It.IsAny<TenantSubscription>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantSubscription s, DateTime now, DateTime? refunded, CancellationToken _) =>
            {
                TenantSubscription? before = _stored.Find(x => x.SubscriptionId == s.SubscriptionId);
                TenantSubscription saved = s with
                {
                    PastDueSince = s.Status is SubscriptionStatus.PastDue or SubscriptionStatus.OnHold ? now : null,
                    RefundedThrough = refunded ?? before?.RefundedThrough,
                    DuplicateAt = before?.DuplicateAt,
                    DuplicateRefundPaymentId = before?.DuplicateRefundPaymentId,
                    CreatedAt = before?.CreatedAt ?? now
                };
                _stored.RemoveAll(x => x.SubscriptionId == s.SubscriptionId);
                _stored.Add(saved);
                return saved;
            });
        _billing.Setup(b => b.GetSubscriptionsAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync(() => _stored.ToList());
        _billing.Setup(b => b.ClaimDuplicateRefundAsync("dodo", It.IsAny<string>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _planService.Setup(p => p.SetAsync(TenantRef, It.IsAny<SetTenantPlanRequest>(), It.IsAny<PlanSource>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, SetTenantPlanRequest r, PlanSource source, CancellationToken _) => new TenantPlanResponse(TenantRef, r.PlanCode, r.LimitOverrides, 2, Now, source));
    }

    private TenantPlan Tenant { get; set; } = new(9, TenantRef, PlanCode.Free, new PlanLimitOverrides { Clients = 40 }, 1, null);

    // ----- Signature -------------------------------------------------------------------------------

    [Fact]
    public void A_signature_holds_for_its_body_and_nothing_else()
    {
        string timestamp = Now.ToUnixTimeSeconds().ToString();
        string header = StandardWebhookSignature.Sign(Secret, "msg_1", timestamp, "{\"a\":1}");

        Assert.True(StandardWebhookSignature.IsValid(Secret, "msg_1", timestamp, "{\"a\":1}", header, Now));
        Assert.True(StandardWebhookSignature.IsValid(Secret, "msg_1", timestamp, "{\"a\":1}", "v1,AAAA " + header, Now));
        Assert.False(StandardWebhookSignature.IsValid(Secret, "msg_1", timestamp, "{\"a\":2}", header, Now));
        Assert.False(StandardWebhookSignature.IsValid(Secret, "msg_2", timestamp, "{\"a\":1}", header, Now));
        Assert.False(StandardWebhookSignature.IsValid("whsec_" + Convert.ToBase64String(new byte[24]), "msg_1", timestamp, "{\"a\":1}", header, Now));
        Assert.False(StandardWebhookSignature.IsValid(Secret, "msg_1", timestamp, "{\"a\":1}", header.Replace("v1,", "v2,"), Now));
    }

    [Fact]
    public void A_signature_outside_five_minutes_of_now_is_refused_so_a_captured_delivery_cannot_be_replayed()
    {
        string old = Now.AddMinutes(-6).ToUnixTimeSeconds().ToString();
        string recent = Now.AddMinutes(-4).ToUnixTimeSeconds().ToString();

        Assert.False(StandardWebhookSignature.IsValid(Secret, "m", old, "{}", StandardWebhookSignature.Sign(Secret, "m", old, "{}"), Now));
        Assert.True(StandardWebhookSignature.IsValid(Secret, "m", recent, "{}", StandardWebhookSignature.Sign(Secret, "m", recent, "{}"), Now));
        Assert.False(StandardWebhookSignature.IsValid(Secret, "m", "not-a-number", "{}", "v1,AAAA", Now));
    }

    // ----- What a subscription puts the tenant on ----------------------------------------------------

    [Theory]
    [InlineData(SubscriptionStatus.Active, 0, null, PlanCode.Pro)]
    [InlineData(SubscriptionStatus.PastDue, 3, null, PlanCode.Pro)]
    [InlineData(SubscriptionStatus.PastDue, 8, null, PlanCode.Free)]
    [InlineData(SubscriptionStatus.OnHold, 8, null, PlanCode.Free)]
    [InlineData(SubscriptionStatus.Cancelled, 0, 5, PlanCode.Pro)]
    [InlineData(SubscriptionStatus.Cancelled, 0, -1, PlanCode.Free)]
    [InlineData(SubscriptionStatus.Failed, 0, null, PlanCode.Free)]
    [InlineData(SubscriptionStatus.Expired, 0, null, PlanCode.Free)]
    [InlineData(SubscriptionStatus.Paused, 0, null, PlanCode.Free)]
    [InlineData(SubscriptionStatus.Pending, 0, null, null)]
    public void A_subscription_keeps_its_plan_while_paid_within_grace_or_inside_a_cancelled_period(SubscriptionStatus status, int pastDueDays, int? periodEndDays, PlanCode? expected)
    {
        DateTime now = Now.UtcDateTime;
        var subscription = Subscription(status, periodEnd: now.AddDays(periodEndDays ?? 20)) with
        {
            PastDueSince = pastDueDays > 0 ? now.AddDays(-pastDueDays) : null
        };

        Assert.Equal(expected, SubscriptionPlan.Decide(subscription, now, TimeSpan.FromDays(7)));
    }

    [Fact]
    public void A_refunded_period_withholds_the_plan_until_the_subscription_is_paid_past_it()
    {
        DateTime now = Now.UtcDateTime;
        DateTime refundedThrough = SubscriptionPlan.RefundedThrough(now.AddDays(-2), BillingInterval.Monthly);

        var refunded = Subscription(SubscriptionStatus.Active, periodEnd: now.AddDays(28)) with { RefundedThrough = refundedThrough };
        var renewed = refunded with { CurrentPeriodEnd = now.AddDays(58) };

        Assert.Equal(PlanCode.Free, SubscriptionPlan.Decide(refunded, now, TimeSpan.FromDays(7)));
        Assert.Equal(PlanCode.Pro, SubscriptionPlan.Decide(renewed, now, TimeSpan.FromDays(7)));
    }

    // ----- Sync --------------------------------------------------------------------------------------

    [Fact]
    public async Task An_active_subscription_moves_the_tenant_to_its_plan_and_keeps_its_overrides()
    {
        ProviderSays(new ProviderSubscription("sub_1", SubscriptionStatus.Active, "prod_maker_y", "cus_1", TenantRef, Now.AddYears(1), false));

        var result = await Sync().SyncSubscriptionAsync("sub_1", refundedThrough: null, CancellationToken.None);

        Assert.Null(result.Reason);
        _billing.Verify(b => b.UpsertSubscriptionAsync(
            It.Is<TenantSubscription>(s => s.TenantId == 9 && s.PlanCode == PlanCode.Maker && s.Interval == BillingInterval.Yearly && s.CustomerId == "cus_1"),
            It.IsAny<DateTime>(), null, It.IsAny<CancellationToken>()));
        _planService.Verify(p => p.SetAsync(TenantRef,
            It.Is<SetTenantPlanRequest>(r => r.PlanCode == PlanCode.Maker && r.LimitOverrides!.Clients == 40 && r.ExpectedRevision == 1),
            It.IsAny<PlanSource>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task A_failed_renewal_tells_the_billing_admins_until_when_the_plan_holds_and_a_paid_one_tells_nobody()
    {
        ProviderSays(new ProviderSubscription("sub_1", SubscriptionStatus.PastDue, "prod_pro_m", "cus_1", TenantRef, Now.AddDays(3), false));

        await Sync().SyncSubscriptionAsync("sub_1", refundedThrough: null, CancellationToken.None);

        _notifier.Verify(n => n.NotifyAsync(9, It.Is<BillingNotice>(notice =>
            notice.Kind == BillingNoticeKind.PaymentFailed
            && notice.PeriodKey == "sub_1:" + Now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff")
            && notice.Paragraphs.Any(p => p.Contains("keeps Pro until " + Now.UtcDateTime.AddDays(7).ToString("d MMMM yyyy")))), It.IsAny<CancellationToken>()), Times.Once);

        ProviderSays(new ProviderSubscription("sub_1", SubscriptionStatus.Active, "prod_pro_m", "cus_1", TenantRef, Now.AddMonths(1), false));
        await Sync().SyncSubscriptionAsync("sub_1", refundedThrough: null, CancellationToken.None);

        _notifier.Verify(n => n.NotifyAsync(It.IsAny<int>(), It.IsAny<BillingNotice>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_notice_that_cannot_be_sent_does_not_fail_the_sync()
    {
        _notifier.Setup(n => n.NotifyAsync(It.IsAny<int>(), It.IsAny<BillingNotice>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("relay"));
        ProviderSays(new ProviderSubscription("sub_1", SubscriptionStatus.PastDue, "prod_pro_m", "cus_1", TenantRef, Now.AddDays(3), false));

        var result = await Sync().SyncSubscriptionAsync("sub_1", refundedThrough: null, CancellationToken.None);

        Assert.Null(result.Reason);
    }

    [Fact]
    public async Task A_plan_changed_meanwhile_is_read_again_and_written_over()
    {
        ProviderSays(new ProviderSubscription("sub_1", SubscriptionStatus.Active, "prod_pro_m", "cus_1", TenantRef, Now.AddMonths(1), false));
        _planService.SetupSequence(p => p.SetAsync(TenantRef, It.IsAny<SetTenantPlanRequest>(), It.IsAny<PlanSource>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Error.Conflict("TENANT_PLAN_CHANGED", "changed"))
            .ReturnsAsync(new TenantPlanResponse(TenantRef, PlanCode.Pro, null, 3, Now));

        await Sync().SyncSubscriptionAsync("sub_1", refundedThrough: null, CancellationToken.None);

        _planService.Verify(p => p.SetAsync(TenantRef, It.Is<SetTenantPlanRequest>(r => r.PlanCode == PlanCode.Pro), It.IsAny<PlanSource>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task An_older_subscription_ending_leaves_the_plan_to_the_tenants_newer_live_one()
    {
        _stored.Add(Subscription(SubscriptionStatus.Active, Now.UtcDateTime.AddDays(20)) with { SubscriptionId = "sub_new", CreatedAt = Now.UtcDateTime.AddDays(-3) });
        ProviderSays(new ProviderSubscription("sub_old", SubscriptionStatus.Cancelled, "prod_maker_m", "cus_1", TenantRef, Now.AddDays(-1), true));

        var result = await Sync().SyncSubscriptionAsync("sub_old", refundedThrough: null, CancellationToken.None);

        Assert.Null(result.Reason);
        Assert.Equal(2, _stored.Count);
        _planService.Verify(p => p.SetAsync(TenantRef, It.Is<SetTenantPlanRequest>(r => r.PlanCode == PlanCode.Pro), PlanSource.Subscription, It.IsAny<CancellationToken>()));
        _planService.Verify(p => p.SetAsync(TenantRef, It.Is<SetTenantPlanRequest>(r => r.PlanCode == PlanCode.Free), It.IsAny<PlanSource>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_second_subscription_paid_beside_the_first_is_cancelled_and_the_first_keeps_deciding()
    {
        _stored.Add(Subscription(SubscriptionStatus.Active, Now.UtcDateTime.AddDays(28)) with { SubscriptionId = "sub_first", PlanCode = PlanCode.Maker, CreatedAt = Now.UtcDateTime.AddMinutes(-2) });
        ProviderSays(new ProviderSubscription("sub_second", SubscriptionStatus.Active, "prod_pro_m", "cus_1", TenantRef, Now.AddMonths(1), false));

        await Sync().SyncSubscriptionAsync("sub_second", refundedThrough: null, CancellationToken.None);

        _billing.Verify(b => b.MarkDuplicateSubscriptionAsync("dodo", "sub_second", Now.UtcDateTime, It.IsAny<CancellationToken>()));
        _gateway.Verify(g => g.CancelSubscriptionAsync("sub_second", It.IsAny<CancellationToken>()));
        _gateway.Verify(g => g.CancelSubscriptionAsync("sub_first", It.IsAny<CancellationToken>()), Times.Never);
        _planService.Verify(p => p.SetAsync(TenantRef, It.Is<SetTenantPlanRequest>(r => r.PlanCode == PlanCode.Maker), PlanSource.Subscription, It.IsAny<CancellationToken>()));
        _planService.Verify(p => p.SetAsync(TenantRef, It.Is<SetTenantPlanRequest>(r => r.PlanCode == PlanCode.Pro), It.IsAny<PlanSource>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_duplicates_payment_is_refunded_once_and_a_refused_refund_gives_the_claim_back()
    {
        _stored.Add(Subscription(SubscriptionStatus.Active, Now.UtcDateTime.AddDays(28)) with { SubscriptionId = "sub_first", CreatedAt = Now.UtcDateTime.AddMinutes(-2) });
        _stored.Add(Subscription(SubscriptionStatus.Cancelled, Now.UtcDateTime.AddDays(30)) with { SubscriptionId = "sub_second", DuplicateAt = Now.UtcDateTime, CreatedAt = Now.UtcDateTime });
        ProviderSays(new ProviderSubscription("sub_second", SubscriptionStatus.Cancelled, "prod_pro_m", "cus_1", TenantRef, Now.AddMonths(1), false));
        PaymentSays(new ProviderPayment("pay_2", true, "cus_1", TenantRef, "sub_second", [new PaymentLine("prod_pro_m", 1)], false, Now));
        _gateway.SetupSequence(g => g.RefundPaymentAsync("pay_2", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"))
            .Returns(Task.CompletedTask);

        await Assert.ThrowsAsync<HttpRequestException>(() => Sync().SyncPaymentAsync("pay_2", CancellationToken.None));
        await Sync().SyncPaymentAsync("pay_2", CancellationToken.None);

        _billing.Verify(b => b.ClaimDuplicateRefundAsync("dodo", "sub_second", "pay_2", true, It.IsAny<CancellationToken>()), Times.Once);
        _gateway.Verify(g => g.RefundPaymentAsync("pay_2", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _gateway.Verify(g => g.CancelSubscriptionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_operators_plan_is_left_alone_until_its_end_date()
    {
        Tenant = Tenant with { PlanCode = PlanCode.Business, Source = PlanSource.Operator, EndsAt = Now.UtcDateTime.AddDays(1) };
        ProviderSays(new ProviderSubscription("sub_1", SubscriptionStatus.Active, "prod_maker_m", "cus_1", TenantRef, Now.AddMonths(1), false));

        await Sync().SyncSubscriptionAsync("sub_1", refundedThrough: null, CancellationToken.None);

        VerifyPlanNotSet();
    }

    [Fact]
    public async Task An_ended_operators_plan_goes_back_to_the_subscription_without_its_contract_overrides()
    {
        Tenant = Tenant with { PlanCode = PlanCode.Business, Source = PlanSource.Operator, EndsAt = Now.UtcDateTime.AddDays(-1) };
        ProviderSays(new ProviderSubscription("sub_1", SubscriptionStatus.Active, "prod_maker_m", "cus_1", TenantRef, Now.AddMonths(1), false));

        await Sync().SyncSubscriptionAsync("sub_1", refundedThrough: null, CancellationToken.None);

        _planService.Verify(p => p.SetAsync(TenantRef, It.Is<SetTenantPlanRequest>(r => r.PlanCode == PlanCode.Maker && r.LimitOverrides == null), PlanSource.Subscription, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task An_ended_operators_plan_with_no_subscription_goes_back_to_free()
    {
        Tenant = Tenant with { PlanCode = PlanCode.Pro, Source = PlanSource.Operator, EndsAt = Now.UtcDateTime.AddMinutes(-1) };

        await Sync().ReviewAsync(9, CancellationToken.None);

        _planService.Verify(p => p.SetAsync(TenantRef, It.Is<SetTenantPlanRequest>(r => r.PlanCode == PlanCode.Free && r.LimitOverrides == null), PlanSource.Default, It.IsAny<CancellationToken>()));
        _gateway.Verify(g => g.FindSubscriptionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void The_deciding_subscription_is_the_oldest_billing_then_the_newest_live_then_the_newest()
    {
        DateTime t = Now.UtcDateTime;
        var oldBilling = Subscription(SubscriptionStatus.Active, t.AddDays(9)) with { SubscriptionId = "a", CreatedAt = t.AddDays(-9) };
        var newBilling = oldBilling with { SubscriptionId = "b", CreatedAt = t.AddDays(-1) };
        var cancelling = oldBilling with { SubscriptionId = "c", CancelAtPeriodEnd = true, CreatedAt = t.AddDays(-20) };
        var ended = Subscription(SubscriptionStatus.Expired, t.AddDays(-5)) with { SubscriptionId = "d", CreatedAt = t };
        var duplicate = newBilling with { SubscriptionId = "e", DuplicateAt = t, CreatedAt = t.AddDays(-30) };

        Assert.Equal("a", SubscriptionPlan.Deciding([newBilling, ended, oldBilling, cancelling, duplicate])!.SubscriptionId);
        Assert.Equal("c", SubscriptionPlan.Deciding([ended, cancelling, duplicate])!.SubscriptionId);
        Assert.Equal("d", SubscriptionPlan.Deciding([ended, duplicate])!.SubscriptionId);
        Assert.Null(SubscriptionPlan.Deciding([]));
        Assert.Equal(["b"], SubscriptionPlan.Duplicates([newBilling, oldBilling, cancelling, duplicate]).Select(d => d.SubscriptionId));
    }

    [Theory]
    [InlineData("prod_unknown", true)]
    [InlineData("prod_maker_m", false)]
    public async Task A_subscription_for_no_plan_or_no_tenant_here_is_set_aside(string productId, bool hasTenant)
    {
        ProviderSays(new ProviderSubscription("sub_1", SubscriptionStatus.Active, productId, "cus_1", hasTenant ? TenantRef : null, Now.AddMonths(1), false));

        var result = await Sync().SyncSubscriptionAsync("sub_1", refundedThrough: null, CancellationToken.None);

        Assert.NotNull(result.Reason);
        VerifyPlanNotSet();
    }

    [Fact]
    public async Task A_credit_pack_payment_records_the_purchase_with_its_grant_once()
    {
        PaymentSays(new ProviderPayment("pay_1", true, "cus_1", TenantRef, null, [new PaymentLine("prod_boost", 2)], false, Now));
        OutboxMessage? queued = null;
        _billing.Setup(b => b.RecordCreditPurchaseAsync("dodo", "pay_1", 9, "cus_1", 1_000m, Now.UtcDateTime, Now.UtcDateTime.AddDays(365), It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, int, string, decimal, DateTime, DateTime, OutboxMessage, CancellationToken>((_, _, _, _, _, _, _, m, _) => queued = m)
            .ReturnsAsync(true);

        var result = await Sync().SyncPaymentAsync("pay_1", CancellationToken.None);

        Assert.Null(result.Reason);
        Assert.NotNull(queued);
        Assert.Equal("Wbskt.Events.Billing.TenantCreditsGrantedEvent", queued.MessageType);
        using JsonDocument payload = JsonDocument.Parse(queued.Payload);
        Assert.Equal(1_000m, payload.RootElement.GetProperty("credits").GetDecimal());
        Assert.Equal("dodo", payload.RootElement.GetProperty("source").GetString());
        Assert.Equal("pay_1", payload.RootElement.GetProperty("sourceRef").GetString());
        Assert.Equal([31, 32], payload.RootElement.GetProperty("workspaceIds").EnumerateArray().Select(e => e.GetInt32()));
    }

    [Fact]
    public async Task A_refunded_credit_pack_is_taken_back()
    {
        PaymentSays(new ProviderPayment("pay_1", true, "cus_1", TenantRef, null, [new PaymentLine("prod_boost", 1)], true, Now));
        _billing.Setup(b => b.FindCreditPurchaseAsync("dodo", "pay_1", It.IsAny<CancellationToken>())).ReturnsAsync(new TenantCreditPurchase(9, 500m, null));
        _billing.Setup(b => b.RefundCreditPurchaseAsync("dodo", "pay_1", It.IsAny<DateTime>(), It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Sync().SyncPaymentAsync("pay_1", CancellationToken.None);

        _billing.Verify(b => b.RefundCreditPurchaseAsync("dodo", "pay_1", It.IsAny<DateTime>(),
            It.Is<OutboxMessage>(m => m.MessageType == "Wbskt.Events.Billing.TenantCreditGrantRevokedEvent"), It.IsAny<CancellationToken>()));
        _billing.Verify(b => b.RecordCreditPurchaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_chargeback_holds_a_credit_pack_and_winning_it_releases_it()
    {
        _billing.Setup(b => b.RecordCreditPurchaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _billing.Setup(b => b.SetCreditPurchaseHoldAsync("dodo", "pay_1", It.IsAny<bool>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        _billing.Setup(b => b.FindCreditPurchaseAsync("dodo", "pay_1", It.IsAny<CancellationToken>())).ReturnsAsync(new TenantCreditPurchase(9, 500m, null));
        PaymentSays(new ProviderPayment("pay_1", true, "cus_1", TenantRef, null, [new PaymentLine("prod_boost", 1)], false, Now, PaymentDispute.Open));
        await Sync().SyncPaymentAsync("pay_1", CancellationToken.None);

        _billing.Setup(b => b.FindCreditPurchaseAsync("dodo", "pay_1", It.IsAny<CancellationToken>())).ReturnsAsync(new TenantCreditPurchase(9, 500m, null, Now.UtcDateTime, 1));
        PaymentSays(new ProviderPayment("pay_1", true, "cus_1", TenantRef, null, [new PaymentLine("prod_boost", 1)], false, Now, PaymentDispute.None));
        await Sync().SyncPaymentAsync("pay_1", CancellationToken.None);

        _billing.Verify(b => b.SetCreditPurchaseHoldAsync("dodo", "pay_1", true, It.IsAny<DateTime>(), 0,
            It.Is<OutboxMessage>(m => m.MessageType == "Wbskt.Events.Billing.TenantCreditGrantHoldChangedEvent" && m.Payload.Contains("\"revision\":1")), It.IsAny<CancellationToken>()));
        _billing.Verify(b => b.SetCreditPurchaseHoldAsync("dodo", "pay_1", false, It.IsAny<DateTime>(), 1,
            It.Is<OutboxMessage>(m => m.Payload.Contains("\"held\":false") && m.Payload.Contains("\"revision\":2")), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task A_lost_chargeback_takes_a_pack_back_and_withholds_a_subscriptions_period()
    {
        PaymentSays(new ProviderPayment("pay_1", true, "cus_1", TenantRef, null, [new PaymentLine("prod_boost", 1)], false, Now, PaymentDispute.Lost));
        _billing.Setup(b => b.FindCreditPurchaseAsync("dodo", "pay_1", It.IsAny<CancellationToken>())).ReturnsAsync(new TenantCreditPurchase(9, 500m, null, Now.UtcDateTime, 1));
        _billing.Setup(b => b.RefundCreditPurchaseAsync("dodo", "pay_1", It.IsAny<DateTime>(), It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Tenant = Tenant with { PlanCode = PlanCode.Pro };
        PaymentSays(new ProviderPayment("pay_2", true, "cus_1", TenantRef, "sub_1", [new PaymentLine("prod_pro_m", 1)], false, Now.AddDays(-3), PaymentDispute.Lost));
        ProviderSays(new ProviderSubscription("sub_1", SubscriptionStatus.Active, "prod_pro_m", "cus_1", TenantRef, Now.AddDays(27), false));

        await Sync().SyncPaymentAsync("pay_1", CancellationToken.None);
        await Sync().SyncPaymentAsync("pay_2", CancellationToken.None);

        _billing.Verify(b => b.RefundCreditPurchaseAsync("dodo", "pay_1", It.IsAny<DateTime>(), It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()));
        _planService.Verify(p => p.SetAsync(TenantRef, It.Is<SetTenantPlanRequest>(r => r.PlanCode == PlanCode.Free), PlanSource.Subscription, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task A_failed_credit_pack_payment_grants_nothing()
    {
        PaymentSays(new ProviderPayment("pay_1", false, "cus_1", TenantRef, null, [new PaymentLine("prod_boost", 1)], false, Now));

        var result = await Sync().SyncPaymentAsync("pay_1", CancellationToken.None);

        Assert.NotNull(result.Reason);
        _billing.Verify(b => b.RecordCreditPurchaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_refunded_subscription_payment_marks_its_period_and_drops_the_tenant_to_free()
    {
        Tenant = Tenant with { PlanCode = PlanCode.Pro };
        PaymentSays(new ProviderPayment("pay_2", true, "cus_1", TenantRef, "sub_1", [new PaymentLine("prod_pro_m", 1)], true, Now.AddDays(-3)));
        ProviderSays(new ProviderSubscription("sub_1", SubscriptionStatus.Active, "prod_pro_m", "cus_1", TenantRef, Now.AddDays(27), false));

        await Sync().SyncPaymentAsync("pay_2", CancellationToken.None);

        DateTime expected = SubscriptionPlan.RefundedThrough(Now.UtcDateTime.AddDays(-3), BillingInterval.Monthly);
        _billing.Verify(b => b.UpsertSubscriptionAsync(It.IsAny<TenantSubscription>(), It.IsAny<DateTime>(), expected, It.IsAny<CancellationToken>()));
        _planService.Verify(p => p.SetAsync(TenantRef, It.Is<SetTenantPlanRequest>(r => r.PlanCode == PlanCode.Free), It.IsAny<PlanSource>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task A_review_falls_back_to_the_stored_copy_when_the_provider_no_longer_knows_the_subscription()
    {
        Tenant = Tenant with { PlanCode = PlanCode.Maker };
        _stored.Add(Subscription(SubscriptionStatus.Cancelled, Now.UtcDateTime.AddDays(-1)) with { PlanCode = PlanCode.Maker });

        await Sync().ReviewAsync(9, CancellationToken.None);

        _planService.Verify(p => p.SetAsync(TenantRef, It.Is<SetTenantPlanRequest>(r => r.PlanCode == PlanCode.Free), It.IsAny<PlanSource>(), It.IsAny<CancellationToken>()));
    }

    // ----- Webhooks ----------------------------------------------------------------------------------

    [Fact]
    public async Task A_webhook_with_a_bad_signature_or_for_another_provider_is_refused_before_it_is_stored()
    {
        _gateway.Setup(g => g.ReadWebhook(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>())).Returns((ProviderWebhook?)null);

        var unsigned = await Webhooks().ReceiveAsync("dodo", new Dictionary<string, string>(), "{}", CancellationToken.None);
        var other = await Webhooks().ReceiveAsync("stripe", new Dictionary<string, string>(), "{}", CancellationToken.None);

        Assert.Equal(ErrorType.Unauthorized, unsigned.Error.Type);
        Assert.Equal("BILLING_PROVIDER_NOT_FOUND", other.Error.Code);
        _billing.Verify(b => b.RecordWebhookAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_webhook_already_acted_on_is_answered_without_acting_again()
    {
        Delivered(new ProviderWebhook("evt_1", "subscription.active", "sub_1", null), processedAt: Now.UtcDateTime);

        var result = await Webhooks().ReceiveAsync("dodo", new Dictionary<string, string>(), "{}", CancellationToken.None);

        Assert.True(result.IsSuccess);
        _gateway.Verify(g => g.FindSubscriptionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_webhook_that_fails_is_kept_unprocessed_and_failed_back_for_redelivery()
    {
        Delivered(new ProviderWebhook("evt_1", "subscription.active", "sub_1", null), processedAt: null);
        _gateway.Setup(g => g.FindSubscriptionAsync("sub_1", It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("down"));

        await Assert.ThrowsAsync<HttpRequestException>(() => Webhooks().ReceiveAsync("dodo", new Dictionary<string, string>(), "{}", CancellationToken.None));

        _billing.Verify(b => b.CompleteWebhookAsync(5, false, It.Is<string>(e => e.Contains("down")), It.IsAny<CancellationToken>()));
        _billing.Verify(b => b.CompleteWebhookAsync(5, true, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_webhook_naming_a_payment_syncs_the_payment_and_is_marked_done_with_any_reason_it_was_set_aside()
    {
        Delivered(new ProviderWebhook("evt_1", "payment.succeeded", "sub_1", "pay_9"), processedAt: null);
        _gateway.Setup(g => g.FindPaymentAsync("pay_9", It.IsAny<CancellationToken>())).ReturnsAsync((ProviderPayment?)null);

        var result = await Webhooks().ReceiveAsync("dodo", new Dictionary<string, string>(), "{}", CancellationToken.None);

        Assert.True(result.IsSuccess);
        _billing.Verify(b => b.CompleteWebhookAsync(5, true, It.Is<string>(r => r.Contains("pay_9")), It.IsAny<CancellationToken>()));
    }

    // ----- Checkout ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(null, null, null, "BILLING_CHECKOUT_INVALID")]
    [InlineData(PlanCode.Maker, BillingInterval.Monthly, "Boost", "BILLING_CHECKOUT_INVALID")]
    [InlineData(PlanCode.Maker, null, null, "BILLING_CHECKOUT_INVALID")]
    [InlineData(PlanCode.Pro, BillingInterval.Yearly, null, "BILLING_OFFER_INVALID")]
    [InlineData(PlanCode.Free, BillingInterval.Monthly, null, "BILLING_OFFER_INVALID")]
    [InlineData(null, null, "Mega", "BILLING_OFFER_INVALID")]
    public async Task A_checkout_must_name_one_thing_on_offer(PlanCode? plan, BillingInterval? interval, string? pack, string code)
    {
        var result = await Billing().CheckoutAsync(9, 4, new BillingCheckoutRequest(plan, interval, pack), CancellationToken.None);

        Assert.Equal(code, result.Error.Code);
        _gateway.Verify(g => g.CreateCheckoutAsync(It.IsAny<BillingCheckout>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_tenant_already_subscribed_changes_its_plan_in_the_portal_not_a_second_checkout()
    {
        _stored.Add(Subscription(SubscriptionStatus.Active, Now.UtcDateTime.AddDays(10)));

        var plan = await Billing().CheckoutAsync(9, 4, new BillingCheckoutRequest(PlanCode.Pro, BillingInterval.Monthly), CancellationToken.None);
        _accounts.Setup(a => a.GetByIdAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = 4, Email = "a@example.com", Username = "ann" });
        _gateway.Setup(g => g.CreateCheckoutAsync(It.IsAny<BillingCheckout>(), It.IsAny<CancellationToken>())).ReturnsAsync("https://pay/1");
        var pack = await Billing().CheckoutAsync(9, 4, new BillingCheckoutRequest(CreditPack: "Boost"), CancellationToken.None);

        Assert.Equal("BILLING_SUBSCRIPTION_ACTIVE", plan.Error.Code);
        Assert.True(pack.IsSuccess);
    }

    [Fact]
    public async Task A_checkout_is_paid_by_the_caller_for_the_tenant_and_a_provider_outage_is_a_503()
    {
        _accounts.Setup(a => a.GetByIdAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = 4, Email = "a@example.com", Username = "ann" });
        _gateway.SetupSequence(g => g.CreateCheckoutAsync(It.IsAny<BillingCheckout>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://checkout.example/session")
            .ThrowsAsync(new HttpRequestException("down"));

        var started = await Billing().CheckoutAsync(9, 4, new BillingCheckoutRequest(PlanCode.Maker, BillingInterval.Monthly), CancellationToken.None);
        var down = await Billing().CheckoutAsync(9, 4, new BillingCheckoutRequest(PlanCode.Maker, BillingInterval.Monthly), CancellationToken.None);

        Assert.Equal("https://checkout.example/session", started.Value.CheckoutUrl);
        _gateway.Verify(g => g.CreateCheckoutAsync(new BillingCheckout("prod_maker_m", TenantRef, "a@example.com", "ann", null), It.IsAny<CancellationToken>()));
        Assert.Equal("BILLING_PROVIDER_UNAVAILABLE", down.Error.Code);
        Assert.Equal(ErrorType.Unavailable, down.Error.Type);
    }

    [Fact]
    public async Task A_plan_checkout_still_open_is_handed_out_again_and_a_new_one_is_kept_for_reuse()
    {
        _accounts.Setup(a => a.GetByIdAsync(4, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = 4, Email = "a@example.com", Username = "ann" });
        _gateway.Setup(g => g.CreateCheckoutAsync(It.IsAny<BillingCheckout>(), It.IsAny<CancellationToken>())).ReturnsAsync("https://pay/new");
        _billing.Setup(b => b.FindOpenCheckoutAsync(9, "dodo", "prod_pro_m", Now.UtcDateTime, It.IsAny<CancellationToken>())).ReturnsAsync("https://pay/open");

        var reused = await Billing().CheckoutAsync(9, 4, new BillingCheckoutRequest(PlanCode.Pro, BillingInterval.Monthly), CancellationToken.None);
        var fresh = await Billing().CheckoutAsync(9, 4, new BillingCheckoutRequest(PlanCode.Maker, BillingInterval.Monthly), CancellationToken.None);
        var pack = await Billing().CheckoutAsync(9, 4, new BillingCheckoutRequest(CreditPack: "Boost"), CancellationToken.None);

        Assert.Equal("https://pay/open", reused.Value.CheckoutUrl);
        Assert.Equal("https://pay/new", fresh.Value.CheckoutUrl);
        Assert.Equal("https://pay/new", pack.Value.CheckoutUrl);
        _gateway.Verify(g => g.CreateCheckoutAsync(It.Is<BillingCheckout>(c => c.ProductId == "prod_pro_m"), It.IsAny<CancellationToken>()), Times.Never);
        _billing.Verify(b => b.RecordCheckoutAsync(9, "dodo", "prod_maker_m", "https://pay/new", Now.UtcDateTime, Now.UtcDateTime.AddMinutes(30), It.IsAny<CancellationToken>()));
        _billing.Verify(b => b.RecordCheckoutAsync(9, "dodo", "prod_boost", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Without_credentials_nothing_is_offered_and_checkout_is_unavailable()
    {
        _gateway.SetupGet(g => g.IsConfigured).Returns(false);

        var read = await Billing().GetAsync(9, CancellationToken.None);
        var checkout = await Billing().CheckoutAsync(9, 4, new BillingCheckoutRequest(PlanCode.Maker, BillingInterval.Monthly), CancellationToken.None);

        Assert.Empty(read.Value.Plans);
        Assert.Empty(read.Value.CreditPacks);
        Assert.Equal("BILLING_UNAVAILABLE", checkout.Error.Code);
    }

    [Fact]
    public async Task The_billing_read_lists_what_is_on_offer_and_the_subscription()
    {
        _stored.Add(Subscription(SubscriptionStatus.PastDue, Now.UtcDateTime.AddDays(3)) with { PastDueSince = Now.UtcDateTime });

        var read = await Billing().GetAsync(9, CancellationToken.None);

        Assert.Equal(
            [new PlanOffer(PlanCode.Maker, BillingInterval.Monthly), new PlanOffer(PlanCode.Maker, BillingInterval.Yearly), new PlanOffer(PlanCode.Pro, BillingInterval.Monthly)],
            read.Value.Plans);
        Assert.Equal([new CreditPackOffer("Boost", 500m)], read.Value.CreditPacks);
        Assert.Equal(SubscriptionStatus.PastDue, read.Value.Subscription!.Status);
        Assert.Equal(Now, read.Value.Subscription.PastDueSince);
    }

    // ----- Changing plan -----------------------------------------------------------------------------

    [Theory]
    [InlineData(PlanCode.Pro, BillingInterval.Monthly, PlanCode.Maker, BillingInterval.Monthly, PlanChangeTiming.AtPeriodEnd)]
    [InlineData(PlanCode.Maker, BillingInterval.Monthly, PlanCode.Pro, BillingInterval.Monthly, PlanChangeTiming.Now)]
    [InlineData(PlanCode.Maker, BillingInterval.Monthly, PlanCode.Maker, BillingInterval.Yearly, PlanChangeTiming.Now)]
    [InlineData(PlanCode.Maker, BillingInterval.Yearly, PlanCode.Maker, BillingInterval.Monthly, PlanChangeTiming.AtPeriodEnd)]
    public async Task An_upgrade_changes_the_plan_at_once_and_a_downgrade_at_the_end_of_the_period(
        PlanCode from, BillingInterval fromInterval, PlanCode to, BillingInterval toInterval, PlanChangeTiming timing)
    {
        _stored.Add(Subscription(SubscriptionStatus.Active, Now.UtcDateTime.AddDays(10)) with { PlanCode = from, Interval = fromInterval });
        ProviderSays(new ProviderSubscription("sub_1", SubscriptionStatus.Active, _options.Plans[from][fromInterval], "cus_1", TenantRef, Now.AddDays(10), false));

        var result = await Billing().ChangePlanAsync(9, new ChangePlanRequest(to, toInterval), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _gateway.Verify(g => g.ChangePlanAsync("sub_1", _options.Plans[to][toInterval], timing, It.IsAny<CancellationToken>()), Times.Once);
        _gateway.Verify(g => g.FindSubscriptionAsync("sub_1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_scheduled_downgrade_shows_on_the_billing_read_and_asking_for_the_current_plan_drops_it()
    {
        _stored.Add(Subscription(SubscriptionStatus.Active, Now.UtcDateTime.AddDays(10)));
        ProviderSays(new ProviderSubscription("sub_1", SubscriptionStatus.Active, "prod_pro_m", "cus_1", TenantRef, Now.AddDays(10), false,
            new ProviderScheduledChange("prod_maker_m", Now.AddDays(10))));

        var scheduled = await Billing().ChangePlanAsync(9, new ChangePlanRequest(PlanCode.Maker, BillingInterval.Monthly), CancellationToken.None);
        ProviderSays(new ProviderSubscription("sub_1", SubscriptionStatus.Active, "prod_pro_m", "cus_1", TenantRef, Now.AddDays(10), false));
        var kept = await Billing().ChangePlanAsync(9, new ChangePlanRequest(PlanCode.Pro, BillingInterval.Monthly), CancellationToken.None);

        Assert.Equal((PlanCode.Maker, BillingInterval.Monthly, Now.AddDays(10)),
            (scheduled.Value.Subscription!.ScheduledPlan, scheduled.Value.Subscription.ScheduledInterval, scheduled.Value.Subscription.ScheduledAt));
        Assert.Null(kept.Value.Subscription!.ScheduledPlan);
        _gateway.Verify(g => g.CancelScheduledChangeAsync("sub_1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_upgrade_drops_a_scheduled_downgrade_first_so_the_period_end_does_not_undo_it()
    {
        _stored.Add(Subscription(SubscriptionStatus.Active, Now.UtcDateTime.AddDays(10)) with
        {
            PlanCode = PlanCode.Maker, ProductId = "prod_maker_y", Interval = BillingInterval.Yearly, ScheduledPlan = PlanCode.Maker, ScheduledInterval = BillingInterval.Monthly, ScheduledAt = Now.UtcDateTime.AddDays(10)
        });
        ProviderSays(new ProviderSubscription("sub_1", SubscriptionStatus.Active, "prod_pro_m", "cus_1", TenantRef, Now.AddDays(30), false));
        var order = new List<string>();
        _gateway.Setup(g => g.CancelScheduledChangeAsync("sub_1", It.IsAny<CancellationToken>())).Callback(() => order.Add("cancel")).Returns(Task.CompletedTask);
        _gateway.Setup(g => g.ChangePlanAsync("sub_1", "prod_pro_m", PlanChangeTiming.Now, It.IsAny<CancellationToken>())).Callback(() => order.Add("change")).Returns(Task.CompletedTask);

        var result = await Billing().ChangePlanAsync(9, new ChangePlanRequest(PlanCode.Pro, BillingInterval.Monthly), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["cancel", "change"], order);
    }

    [Fact]
    public async Task A_failed_upgrade_puts_back_the_downgrade_it_dropped_so_the_customer_does_not_renew_on_the_plan_they_left()
    {
        _stored.Add(Subscription(SubscriptionStatus.Active, Now.UtcDateTime.AddDays(10)) with
        {
            PlanCode = PlanCode.Maker, ProductId = "prod_maker_y", Interval = BillingInterval.Yearly, ScheduledPlan = PlanCode.Maker, ScheduledInterval = BillingInterval.Monthly, ScheduledAt = Now.UtcDateTime.AddDays(10)
        });
        _gateway.Setup(g => g.ChangePlanAsync("sub_1", "prod_pro_m", PlanChangeTiming.Now, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("card declined"));

        var result = await Billing().ChangePlanAsync(9, new ChangePlanRequest(PlanCode.Pro, BillingInterval.Monthly), CancellationToken.None);

        Assert.Equal("BILLING_PROVIDER_UNAVAILABLE", result.Error.Code);
        _gateway.Verify(g => g.CancelScheduledChangeAsync("sub_1", It.IsAny<CancellationToken>()), Times.Once);
        _gateway.Verify(g => g.ChangePlanAsync("sub_1", "prod_maker_m", PlanChangeTiming.AtPeriodEnd, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_change_made_but_not_read_back_is_still_a_success_so_it_is_not_asked_for_twice()
    {
        _stored.Add(Subscription(SubscriptionStatus.Active, Now.UtcDateTime.AddDays(10)) with { PlanCode = PlanCode.Maker, ProductId = "prod_maker_m" });
        _gateway.Setup(g => g.FindSubscriptionAsync("sub_1", It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("timeout"));

        var result = await Billing().ChangePlanAsync(9, new ChangePlanRequest(PlanCode.Pro, BillingInterval.Monthly), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _gateway.Verify(g => g.ChangePlanAsync("sub_1", "prod_pro_m", PlanChangeTiming.Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_plan_change_needs_an_offer_a_live_paid_up_subscription_and_the_provider()
    {
        var offer = await Billing().ChangePlanAsync(9, new ChangePlanRequest(PlanCode.Pro, BillingInterval.Yearly), CancellationToken.None);
        var none = await Billing().ChangePlanAsync(9, new ChangePlanRequest(PlanCode.Pro, BillingInterval.Monthly), CancellationToken.None);
        _stored.Add(Subscription(SubscriptionStatus.PastDue, Now.UtcDateTime.AddDays(10)) with { PlanCode = PlanCode.Maker, PastDueSince = Now.UtcDateTime });
        var pastDue = await Billing().ChangePlanAsync(9, new ChangePlanRequest(PlanCode.Pro, BillingInterval.Monthly), CancellationToken.None);
        _stored.Clear();
        _stored.Add(Subscription(SubscriptionStatus.Active, Now.UtcDateTime.AddDays(10)) with { PlanCode = PlanCode.Maker });
        _gateway.Setup(g => g.ChangePlanAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PlanChangeTiming>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("down"));
        var down = await Billing().ChangePlanAsync(9, new ChangePlanRequest(PlanCode.Pro, BillingInterval.Monthly), CancellationToken.None);

        Assert.Equal("BILLING_OFFER_INVALID", offer.Error.Code);
        Assert.Equal("BILLING_SUBSCRIPTION_REQUIRED", none.Error.Code);
        Assert.Equal("BILLING_SUBSCRIPTION_NOT_CHANGEABLE", pastDue.Error.Code);
        Assert.Equal("BILLING_PROVIDER_UNAVAILABLE", down.Error.Code);
    }

    [Fact]
    public async Task Dodo_changes_plan_and_drops_a_scheduled_change_with_the_documented_calls()
    {
        var handler = new StubHandler(request => request.Method == HttpMethod.Delete ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json("{}"));
        var gateway = Dodo(handler);

        await gateway.ChangePlanAsync("sub_1", "prod_pro_m", PlanChangeTiming.Now, CancellationToken.None);
        await gateway.ChangePlanAsync("sub_1", "prod_maker_m", PlanChangeTiming.AtPeriodEnd, CancellationToken.None);
        await gateway.CancelScheduledChangeAsync("sub_1", CancellationToken.None);

        Assert.Equal("/subscriptions/sub_1/change-plan", handler.Requests[0].RequestUri!.AbsolutePath);
        JsonElement upgrade = JsonDocument.Parse(handler.Bodies[0]).RootElement;
        Assert.Equal(("prod_pro_m", 1, "prorated_immediately", "immediately", "prevent_change"),
            (upgrade.GetProperty("product_id").GetString(), upgrade.GetProperty("quantity").GetInt32(), upgrade.GetProperty("proration_billing_mode").GetString(),
             upgrade.GetProperty("effective_at").GetString(), upgrade.GetProperty("on_payment_failure").GetString()));
        JsonElement downgrade = JsonDocument.Parse(handler.Bodies[1]).RootElement;
        Assert.Equal(("full_immediately", "next_billing_date", true),
            (downgrade.GetProperty("proration_billing_mode").GetString(), downgrade.GetProperty("effective_at").GetString(), downgrade.GetProperty("cancel_scheduled_change_plan").GetBoolean()));
        Assert.Equal(HttpMethod.Delete, handler.Requests[2].Method);
        Assert.Equal("/subscriptions/sub_1/change-plan/scheduled", handler.Requests[2].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task The_portal_needs_a_tenant_that_has_paid()
    {
        var none = await Billing().PortalAsync(9, CancellationToken.None);

        Assert.Equal("BILLING_CUSTOMER_NOT_FOUND", none.Error.Code);
    }

    // ----- Dodo's JSON -------------------------------------------------------------------------------

    [Fact]
    public async Task Dodo_subscriptions_and_payments_are_read_with_the_tenant_from_their_metadata()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/subscriptions/sub_1" => Json($$"""
                {"subscription_id":"sub_1","status":"past_due","product_id":"prod_pro_m","customer":{"customer_id":"cus_1"},
                 "metadata":{"tenant_ref":"{{TenantRef}}"},"next_billing_date":"2026-12-09T12:00:00Z","cancel_at_next_billing_date":true,
                 "scheduled_change":{"id":"sc_1","product_id":"prod_maker_m","quantity":1,"effective_at":"2026-12-09T12:00:00Z","created_at":"2026-11-09T12:00:00Z","addons":[]} }
                """),
            "/payments/pay_1" => Json("""
                {"payment_id":"pay_1","status":"succeeded","customer":{"customer_id":"cus_1"},"metadata":{"tenant_ref":42},
                 "subscription_id":null,"product_cart":[{"product_id":"prod_boost","quantity":3}],"refund_status":"full","created_at":"2026-11-01T00:00:00Z"}
                """),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var gateway = Dodo(handler);

        ProviderSubscription? subscription = await gateway.FindSubscriptionAsync("sub_1", CancellationToken.None);
        ProviderPayment? payment = await gateway.FindPaymentAsync("pay_1", CancellationToken.None);
        ProviderPayment? missing = await gateway.FindPaymentAsync("pay_x", CancellationToken.None);

        Assert.Equal(new ProviderSubscription("sub_1", SubscriptionStatus.PastDue, "prod_pro_m", "cus_1", TenantRef, new DateTimeOffset(2026, 12, 9, 12, 0, 0, TimeSpan.Zero), true,
            new ProviderScheduledChange("prod_maker_m", new DateTimeOffset(2026, 12, 9, 12, 0, 0, TimeSpan.Zero))), subscription);
        Assert.NotNull(payment);
        Assert.Null(payment.TenantRef);
        Assert.Null(payment.SubscriptionId);
        Assert.True(payment.Succeeded && payment.FullyRefunded);
        Assert.Equal([new PaymentLine("prod_boost", 3)], payment.Lines);
        Assert.Null(missing);
        Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization?.Scheme);
    }

    [Fact]
    public async Task Dodo_reads_a_payments_latest_dispute_and_cancels_and_refunds_with_the_documented_calls()
    {
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/payments/pay_1" => Json("""
                {"payment_id":"pay_1","status":"succeeded","customer":{"customer_id":"cus_1"},"product_cart":[],"refund_status":null,
                 "disputes":[{"dispute_id":"dis_2","dispute_status":"dispute_lost","created_at":"2026-11-08T00:00:00Z"},
                             {"dispute_id":"dis_1","dispute_status":"dispute_won","created_at":"2026-11-02T00:00:00Z"}]}
                """),
            "/payments/pay_2" => Json("""{"payment_id":"pay_2","status":"succeeded","disputes":[{"dispute_status":"dispute_challenged","created_at":"2026-11-08T00:00:00Z"}]}"""),
            _ => Json("{}")
        });
        var gateway = Dodo(handler);

        ProviderPayment? lost = await gateway.FindPaymentAsync("pay_1", CancellationToken.None);
        ProviderPayment? open = await gateway.FindPaymentAsync("pay_2", CancellationToken.None);
        await gateway.CancelSubscriptionAsync("sub_1", CancellationToken.None);
        await gateway.RefundPaymentAsync("pay_3", "Duplicate", CancellationToken.None);

        Assert.Equal(PaymentDispute.Lost, lost!.Dispute);
        Assert.True(lost.Reversed);
        Assert.Equal(PaymentDispute.Open, open!.Dispute);
        Assert.False(open.Reversed);
        Assert.Equal(HttpMethod.Patch, handler.Requests[2].Method);
        Assert.Equal("/subscriptions/sub_1", handler.Requests[2].RequestUri!.AbsolutePath);
        Assert.Equal("cancelled", JsonDocument.Parse(handler.Bodies[2]).RootElement.GetProperty("status").GetString());
        Assert.Equal("/refunds", handler.Requests[3].RequestUri!.AbsolutePath);
        Assert.Equal("pay_3", JsonDocument.Parse(handler.Bodies[3]).RootElement.GetProperty("payment_id").GetString());
    }

    [Theory]
    [InlineData("cancelled", false)]
    [InlineData("active", true)]
    public async Task Cancelling_a_subscription_dodo_already_ended_is_not_a_failure(string status, bool throws)
    {
        var handler = new StubHandler(request => request.Method == HttpMethod.Patch
            ? new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            : Json($$"""{"subscription_id":"sub_1","status":"{{status}}","product_id":"prod_pro_m","next_billing_date":"2026-12-09T12:00:00Z"}"""));
        var gateway = Dodo(handler);

        Exception? thrown = await Record.ExceptionAsync(() => gateway.CancelSubscriptionAsync("sub_1", CancellationToken.None));

        Assert.Equal(throws, thrown is HttpRequestException);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public async Task A_key_the_provider_refuses_is_reported_at_start(HttpStatusCode status, bool accepted)
    {
        var gateway = Dodo(new StubHandler(_ => new HttpResponseMessage(status) { Content = new StringContent("{}") }));

        Assert.Equal(accepted, await gateway.AcceptsCredentialsAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("test_mode", true)]
    [InlineData("live_mode", true)]
    [InlineData("live", false)]
    public void An_unknown_dodo_mode_stops_the_host_at_start(string mode, bool valid)
    {
        var options = new DodoOptions { Environment = mode };

        if (valid)
        {
            options.EnsureValid();
        }
        else
        {
            Assert.Throws<InvalidOperationException>(options.EnsureValid);
        }
    }

    [Fact]
    public async Task A_dodo_checkout_carries_the_tenant_in_its_metadata()
    {
        var handler = new StubHandler(_ => Json("""{"session_id":"cks_1","checkout_url":"https://test.checkout.dodopayments.com/cks_1"}"""));

        string url = await Dodo(handler).CreateCheckoutAsync(new BillingCheckout("prod_maker_m", TenantRef, "a@example.com", "ann", "https://console.example"), CancellationToken.None);

        Assert.Equal("https://test.checkout.dodopayments.com/cks_1", url);
        using JsonDocument sent = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal("prod_maker_m", sent.RootElement.GetProperty("product_cart")[0].GetProperty("product_id").GetString());
        Assert.Equal(TenantRef.ToString(), sent.RootElement.GetProperty("metadata").GetProperty("tenant_ref").GetString());
        Assert.Equal("a@example.com", sent.RootElement.GetProperty("customer").GetProperty("email").GetString());
        Assert.Equal("https://console.example", sent.RootElement.GetProperty("return_url").GetString());
    }

    [Fact]
    public void A_dodo_webhook_is_read_only_with_a_valid_signature()
    {
        const string body = """{"business_id":"bus_1","type":"payment.succeeded","timestamp":"2026-11-09T12:00:00Z","data":{"payload_type":"Payment","payment_id":"pay_1","subscription_id":"sub_1"}}""";
        string timestamp = Now.ToUnixTimeSeconds().ToString();
        var headers = new Dictionary<string, string>
        {
            ["webhook-id"] = "evt_1",
            ["webhook-timestamp"] = timestamp,
            ["webhook-signature"] = StandardWebhookSignature.Sign(Secret, "evt_1", timestamp, body)
        };
        var gateway = Dodo(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        Assert.Equal(new ProviderWebhook("evt_1", "payment.succeeded", "sub_1", "pay_1"), gateway.ReadWebhook(headers, body));
        Assert.Null(gateway.ReadWebhook(headers, body.Replace("pay_1", "pay_2")));
        Assert.Null(gateway.ReadWebhook(new Dictionary<string, string>(headers) { ["webhook-id"] = "evt_2" }, body));
    }

    // ----- Helpers -----------------------------------------------------------------------------------

    private TenantBillingSync Sync() => new(
        _gateway.Object, _billing.Object, _plans.Object, _planService.Object, _audit.Object, _notifier.Object,
        Options.Create(_options), new FixedClock(Now), NullLogger<TenantBillingSync>.Instance);

    private BillingWebhookService Webhooks() => new(_gateway.Object, _billing.Object, Sync(), new AuthMetrics(), NullLogger<BillingWebhookService>.Instance);

    private TenantBillingService Billing() => new(
        _gateway.Object, _billing.Object, _plans.Object, _accounts.Object, Options.Create(_options), new FixedClock(Now), Sync(), NullLogger<TenantBillingService>.Instance);

    private static DodoPaymentGateway Dodo(StubHandler handler)
    {
        var options = new DodoOptions { ApiKey = "test-key", WebhookSecret = Secret };
        var http = new HttpClient(handler) { BaseAddress = options.ResolveBaseAddress() };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.ApiKey);
        return new DodoPaymentGateway(http, Options.Create(options), new FixedClock(Now), NullLogger<DodoPaymentGateway>.Instance);
    }

    private void ProviderSays(ProviderSubscription subscription) =>
        _gateway.Setup(g => g.FindSubscriptionAsync(subscription.Id, It.IsAny<CancellationToken>())).ReturnsAsync(subscription);

    private void PaymentSays(ProviderPayment payment) =>
        _gateway.Setup(g => g.FindPaymentAsync(payment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(payment);

    private void Delivered(ProviderWebhook webhook, DateTime? processedAt)
    {
        _gateway.Setup(g => g.ReadWebhook(It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>())).Returns(webhook);
        _billing.Setup(b => b.RecordWebhookAsync("dodo", webhook.EventId, webhook.Type, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BillingWebhookDelivery(5, processedAt));
    }

    private void VerifyPlanNotSet() =>
        _planService.Verify(p => p.SetAsync(It.IsAny<Guid>(), It.IsAny<SetTenantPlanRequest>(), It.IsAny<PlanSource>(), It.IsAny<CancellationToken>()), Times.Never);

    private static TenantSubscription Subscription(SubscriptionStatus status, DateTime periodEnd) =>
        new(9, "dodo", "sub_1", "cus_1", "prod_pro_m", PlanCode.Pro, BillingInterval.Monthly, status, periodEnd, false, null, null);

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return respond(request);
        }
    }
}

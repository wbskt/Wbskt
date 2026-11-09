using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.Auth.Host.Handlers;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services.Billing;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Auth.Models;
using Wbskt.Events.Auth;
using Wbskt.Events.Billing;
using Wbskt.Infrastructure.Billing;
using Wbskt.Models.Billing;
using Wbskt.Primitives.Constants;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// The billing emails (#694): each kind goes to the tenant's billing admins once per period, however
/// often it is reported, through the real notifier over an in-memory claim table and a recording mailer.
/// </summary>
public sealed class BillingNoticeTests
{
    private const int TenantId = 9;
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<ITenantBillingProvider> _billing = new();
    private readonly Mock<IAccessControlProvider> _access = new();
    private readonly Mock<ITenantPlanProvider> _plans = new();
    private readonly Mock<IWorkspaceProvider> _workspaces = new();
    private readonly Mock<IAuthMailer> _mailer = new();
    private readonly HashSet<(int, BillingNoticeKind, string)> _claimed = [];
    private readonly List<(string To, BillingNotice Notice, string Url)> _sent = [];
    private readonly BillingOptions _options = new() { ReturnUrl = "https://console.example/billing" };

    public BillingNoticeTests()
    {
        _mailer.SetupGet(m => m.IsConfigured).Returns(true);
        _mailer.Setup(m => m.QueueBillingNoticeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<BillingNotice>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string to, string _, BillingNotice notice, string url, CancellationToken _) => _sent.Add((to, notice, url)))
            .Returns(ValueTask.CompletedTask);
        _billing.Setup(b => b.ClaimBillingNoticeAsync(It.IsAny<int>(), It.IsAny<BillingNoticeKind>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int tenantId, BillingNoticeKind kind, string period, CancellationToken _) => _claimed.Add((tenantId, kind, period)));
        _billing.Setup(b => b.GetBillingContactsAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new BillingContact(1, "owner", "owner@example.com"), new BillingContact(2, "dev", "dev@example.com")]);
        _access.Setup(a => a.VerifyPermissionAsync(1, TenantId, null, PermissionNames.BillingManage, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _access.Setup(a => a.VerifyPermissionAsync(2, TenantId, null, PermissionNames.BillingManage, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        foreach (int workspaceId in new[] { 31, 32 })
        {
            _plans.Setup(p => p.GetWorkspaceAccountsAsync(null, 1, null, workspaceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync([new WorkspaceAccountResponse(workspaceId, TenantId, Guid.NewGuid(), PlanCode.Free, null, 4)]);
        }

        _workspaces.Setup(w => w.FindByIdAsync(31, It.IsAny<CancellationToken>())).ReturnsAsync(new Workspace { Id = 31, Name = "<Greenhouse>" });
    }

    [Fact]
    public async Task Every_kind_is_sent_once_per_period_to_the_billing_admins_only()
    {
        BillingNoticeConsumer consumer = Consumer();
        DateTime nextMonth = Now.AddMonths(1);

        // Every report twice, and the account's messages from both of its workspaces.
        for (int round = 0; round < 2; round++)
        {
            await consumer.Consume(Context(new TenantCreditAllowanceReachedEvent(31, TenantId, 80, 2000m, 1600m) { CreatedAtUtc = Now }));
            await consumer.Consume(Context(new TenantCreditAllowanceReachedEvent(31, TenantId, 100, 2000m, 2000m) { CreatedAtUtc = Now }));
            await consumer.Consume(Context(new WorkspaceCreditCapReachedEvent(31, 80, 500m, 400m) { CreatedAtUtc = Now }));
            await consumer.Consume(Context(new WorkspaceCreditCapReachedEvent(31, 100, 500m, 500m) { CreatedAtUtc = Now }));
            foreach (int workspaceId in new[] { 31, 32 })
            {
                await consumer.Consume(Context(new WorkspaceMessageLimitApproachingEvent(workspaceId, 1_500_000, 1_200_000) { CreatedAtUtc = Now }));
                await consumer.Consume(Context(new WorkspaceMessageLimitReachedEvent(workspaceId, 1_500_000, 1_500_000, true) { CreatedAtUtc = Now }));
                await consumer.Consume(Context(new WorkspaceMessagesThrottledEvent(workspaceId, 1_500_000, 3_000_000, 3_000_000) { CreatedAtUtc = Now }));
            }

            await consumer.Consume(Context(new TenantPlanChangedEvent(TenantId, Guid.NewGuid(), PlanCode.Free, PlanCode.Pro, 5) { CreatedAtUtc = Now }));
            await Notifier().NotifyAsync(TenantId, BillingNotices.PaymentFailed("sub_1", PlanCode.Pro, Now, TimeSpan.FromDays(7)), CancellationToken.None);
            await Worker().TickAsync(CancellationToken.None);
        }

        Assert.Equal(Enum.GetValues<BillingNoticeKind>().Order(), _sent.Select(s => s.Notice.Kind).Order());
        Assert.All(_sent, s => Assert.Equal("owner@example.com", s.To));
        Assert.All(_sent, s => Assert.Equal("https://console.example/billing", s.Url));

        // A new month is a new period for the month's warnings.
        await consumer.Consume(Context(new TenantCreditAllowanceReachedEvent(31, TenantId, 80, 2000m, 1600m) { CreatedAtUtc = nextMonth }));
        await consumer.Consume(Context(new WorkspaceMessageLimitApproachingEvent(32, 1_500_000, 1_200_000) { CreatedAtUtc = nextMonth }));
        Assert.Equal(Enum.GetValues<BillingNoticeKind>().Length + 2, _sent.Count);
    }

    [Fact]
    public async Task A_cap_is_its_workspaces_own_so_two_workspaces_each_hear_of_theirs()
    {
        BillingNoticeConsumer consumer = Consumer();

        await consumer.Consume(Context(new WorkspaceCreditCapReachedEvent(31, 100, 500m, 500m) { CreatedAtUtc = Now }));
        await consumer.Consume(Context(new WorkspaceCreditCapReachedEvent(32, 100, 500m, 500m) { CreatedAtUtc = Now }));

        Assert.Equal(["2026-11:31", "2026-11:32"], _sent.Select(s => s.Notice.PeriodKey).Order());
        Assert.Contains("<Greenhouse>", _sent[0].Notice.Subject);
    }

    [Fact]
    public async Task An_upgrade_or_a_change_of_limits_alone_is_not_announced()
    {
        BillingNoticeConsumer consumer = Consumer();

        await consumer.Consume(Context(new TenantPlanChangedEvent(TenantId, Guid.NewGuid(), PlanCode.Pro, PlanCode.Maker, 5)));
        await consumer.Consume(Context(new TenantPlanChangedEvent(TenantId, Guid.NewGuid(), PlanCode.Pro, PlanCode.Pro, 6)));

        Assert.Empty(_sent);
    }

    [Fact]
    public void A_downgrade_names_the_limits_that_drop_and_when_older_history_goes()
    {
        BillingNotice notice = BillingNotices.Downgraded(PlanCode.Pro, PlanCatalog.BuiltIn[PlanCode.Pro], PlanCode.Maker, PlanCatalog.BuiltIn[PlanCode.Maker], 5, Now.AddDays(30));

        Assert.Contains("Clients: 500 → 100", notice.Paragraphs);
        Assert.Contains("Workflow credits a month: 150,000 → 25,000", notice.Paragraphs);
        Assert.Contains(notice.Paragraphs, p => p.Contains("raw readings for 14 days (was 30)") && p.Contains("until 9 December 2026"));
        Assert.Contains(notice.Paragraphs, p => p.Contains("hourly readings for 365 days (was 730)") && !p.Contains("run history for 30"));
    }

    [Theory]
    [InlineData(PlanCode.Free, "one message a minute")]
    [InlineData(PlanCode.Pro, "Far past it they are slowed")]
    [InlineData(PlanCode.Business, "your contract covers")]
    public void The_message_warning_says_what_the_plan_does_past_its_allowance(PlanCode plan, string expected)
    {
        Assert.Contains(BillingNotices.MessagesApproaching(plan, 100, 80, Now).Paragraphs, p => p.Contains(expected));
        Assert.DoesNotContain(BillingNotices.MessagesReached(PlanCode.Business, 100, 100, throttled: false, Now).Paragraphs, p => p.Contains("slowed"));
    }

    [Fact]
    public async Task Nothing_is_claimed_while_there_is_no_relay_or_no_one_to_write_to_so_it_is_sent_once_there_is()
    {
        BillingNotice notice = BillingNotices.PackExpiring("pay_1", 500m, Now.AddDays(10));
        _mailer.SetupGet(m => m.IsConfigured).Returns(false);
        await Notifier().NotifyAsync(TenantId, notice, CancellationToken.None);

        _mailer.SetupGet(m => m.IsConfigured).Returns(true);
        _access.Setup(a => a.VerifyPermissionAsync(1, TenantId, null, PermissionNames.BillingManage, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await Notifier().NotifyAsync(TenantId, notice, CancellationToken.None);
        Assert.Empty(_claimed);

        _access.Setup(a => a.VerifyPermissionAsync(2, TenantId, null, PermissionNames.BillingManage, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Notifier().NotifyAsync(TenantId, notice, CancellationToken.None);
        Assert.Equal(["dev@example.com"], _sent.Select(s => s.To));
    }

    [Fact]
    public async Task The_worker_looks_for_grace_periods_ending_within_two_days_and_packs_expiring_within_thirty()
    {
        await Worker().TickAsync(CancellationToken.None);

        _billing.Verify(b => b.GetPastDueBetweenAsync(Now.AddDays(-7), Now.AddDays(-5), It.IsAny<CancellationToken>()));
        _billing.Verify(b => b.GetCreditPurchasesExpiringAsync(Now, Now.AddDays(30), It.IsAny<CancellationToken>()));
        Assert.Contains(_sent, s => s.Notice is { Kind: BillingNoticeKind.GraceEnding, Subject: "Your account moves to Free on 10 November 2026" });
        Assert.Contains(_sent, s => s.Notice is { Kind: BillingNoticeKind.PackExpiring, PeriodKey: "pay_1" });
    }

    [Fact]
    public void The_email_encodes_what_the_tenant_wrote_and_links_to_the_billing_page()
    {
        BillingNotice notice = BillingNotices.CreditCap(31, "<script>x</script>", 100, 500m, 500m, Now);

        OutboundMail mail = AuthEmailTemplates.Billing(new AuthEmailOptions(), "owner@example.com", "owner", notice, "https://console.example/billing");

        Assert.DoesNotContain("<script>", mail.HtmlBody);
        Assert.Contains("&lt;script&gt;", mail.HtmlBody);
        Assert.Contains("href=\"https://console.example/billing\"", mail.HtmlBody);
        Assert.Contains("until 1 December 2026", mail.HtmlBody);
    }

    private BillingNotifier Notifier() => new(
        _billing.Object, _access.Object, _mailer.Object, Options.Create(_options), Options.Create(new AuthEmailOptions()), NullLogger<BillingNotifier>.Instance);

    private BillingNoticeConsumer Consumer() => new(
        Notifier(), _plans.Object, _workspaces.Object, new PlanCatalog(Options.Create(new PlanCatalogOptions())), new ConfigurationBuilder().Build());

    private BillingNoticeWorker Worker()
    {
        _billing.Setup(b => b.GetPastDueBetweenAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PastDueSubscription(TenantId, "sub_1", PlanCode.Pro, Now.AddDays(-6))]);
        _billing.Setup(b => b.GetCreditPurchasesExpiringAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ExpiringCreditPurchase(TenantId, "pay_1", 500m, Now.AddDays(12))]);
        var services = new ServiceCollection()
            .AddSingleton(_billing.Object)
            .AddSingleton<IBillingNotifier>(Notifier())
            .AddSingleton(Options.Create(new BillingOptions()))
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .BuildServiceProvider();
        return new BillingNoticeWorker(services.GetRequiredService<IServiceScopeFactory>(), new FixedClock(), NullLogger<BillingNoticeWorker>.Instance);
    }

    private static ConsumeContext<T> Context<T>(T message) where T : class
    {
        var context = new Mock<ConsumeContext<T>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context.Object;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
    }
}

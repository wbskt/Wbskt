using Wbskt.Auth.Host.Services.Email;
using Wbskt.Auth.Host.Telemetry;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Services.Billing;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure.Events.Outbox;
using Wbskt.Infrastructure.Security;
using Wbskt.Models.Billing;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// Deleting your own account: the password is asked again, the subscriptions of tenants only you belong
/// to are cancelled first, the workspaces of those tenants go with the account, and the access tokens
/// already issued stop working.
/// </summary>
public sealed class AccountDeletionServiceTests
{
    private const string Password = "correct horse battery";
    private const string Ip = "10.0.0.1";
    private static readonly Guid UserRef = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Mock<IAccountProvider> _accounts = new();
    private readonly Mock<IEventBus> _bus = new();
    private readonly RecordingAccessChanges _accessChanges = new();
    private readonly Mock<IAccessTokenRevocation> _accessTokens = new();
    private readonly Mock<ITenantBillingProvider> _billing = new();
    private readonly Mock<IPaymentGateway> _gateway = new();

    public AccountDeletionServiceTests()
    {
        _accounts.Setup(p => p.GetByIdAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(AUser());
        _billing.Setup(b => b.GetLiveSubscriptionsOfSoleTenantsAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _accounts.Setup(p => p.GetDeletionBlockersAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _gateway.SetupGet(g => g.Name).Returns("dodo");
        _gateway.SetupGet(g => g.IsConfigured).Returns(true);
    }

    [Fact]
    public async Task A_sole_tenants_subscription_is_cancelled_before_the_account_is_deleted()
    {
        _billing.Setup(b => b.GetLiveSubscriptionsOfSoleTenantsAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync([Subscription("sub_1")]);
        _accounts.Setup(p => p.GetSoleTenantWorkspaceIdsAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(new List<int> { 7 });
        var order = new List<string>();
        _gateway.Setup(g => g.CancelSubscriptionAsync("sub_1", It.IsAny<CancellationToken>())).Callback(() => order.Add("cancel")).Returns(Task.CompletedTask);
        _accounts.Setup(p => p.DeleteUserAsync(42, It.IsAny<IReadOnlyDictionary<int, OutboxMessage>>(), null, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("delete"))
            .ReturnsAsync(new List<int>());

        var result = await Service().DeleteAccountAsync(42, Password, Ip);

        Assert.True(result.IsSuccess);
        Assert.Equal(["cancel", "delete"], order);
    }

    /// <summary>
    /// The only administrator of a tenant others belong to cannot delete the account, and finding that
    /// out must not cost them the subscription of the tenant that is theirs alone.
    /// </summary>
    [Fact]
    public async Task A_deletion_that_will_be_refused_cancels_no_subscription()
    {
        _billing.Setup(b => b.GetLiveSubscriptionsOfSoleTenantsAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync([Subscription("sub_1")]);
        _accounts.Setup(p => p.GetDeletionBlockersAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(["Acme", "Globex"]);

        var result = await Service().DeleteAccountAsync(42, Password, Ip);

        Assert.Equal("ACCOUNT_SOLE_ADMINISTRATOR", result.Error.Code);
        Assert.Equal(Wbskt.Infrastructure.ErrorType.Conflict, result.Error.Type);
        Assert.Contains("Acme, Globex", result.Error.Message);
        _gateway.Verify(g => g.CancelSubscriptionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _accounts.Verify(p => p.DeleteUserAsync(It.IsAny<int>(), It.IsAny<IReadOnlyDictionary<int, OutboxMessage>>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task When_the_provider_cannot_cancel_nothing_is_deleted_and_the_caller_retries()
    {
        _billing.Setup(b => b.GetLiveSubscriptionsOfSoleTenantsAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync([Subscription("sub_1")]);
        _gateway.Setup(g => g.CancelSubscriptionAsync("sub_1", It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("down"));

        var down = await Service().DeleteAccountAsync(42, Password, Ip);
        _gateway.Setup(g => g.CancelSubscriptionAsync("sub_1", It.IsAny<CancellationToken>())).ThrowsAsync(new TaskCanceledException("timed out"));
        var timedOut = await Service().DeleteAccountAsync(42, Password, Ip);
        _gateway.SetupGet(g => g.IsConfigured).Returns(false);
        var unconfigured = await Service().DeleteAccountAsync(42, Password, Ip);

        Assert.Equal("BILLING_SUBSCRIPTION_NOT_CANCELLED", down.Error.Code);
        Assert.Equal(Wbskt.Infrastructure.ErrorType.Unavailable, down.Error.Type);
        Assert.Equal("BILLING_SUBSCRIPTION_NOT_CANCELLED", timedOut.Error.Code);
        Assert.Equal("BILLING_SUBSCRIPTION_NOT_CANCELLED", unconfigured.Error.Code);
        _accounts.Verify(p => p.DeleteUserAsync(It.IsAny<int>(), It.IsAny<IReadOnlyDictionary<int, OutboxMessage>>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
        _accessTokens.Verify(a => a.RevokeUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Deleting_takes_the_sole_tenant_workspaces_and_revokes_the_access_tokens()
    {
        _accounts.Setup(p => p.GetSoleTenantWorkspaceIdsAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(new List<int> { 7, 9 });
        IReadOnlyDictionary<int, OutboxMessage>? planned = null;
        _accounts.Setup(p => p.DeleteUserAsync(42, It.IsAny<IReadOnlyDictionary<int, OutboxMessage>>(), null, It.IsAny<CancellationToken>()))
            .Callback<int, IReadOnlyDictionary<int, OutboxMessage>, DateTime?, CancellationToken>((_, w, _, _) => planned = w)
            .ReturnsAsync(new List<int> { 3 });

        var result = await Service().DeleteAccountAsync(42, Password, Ip);

        Assert.True(result.IsSuccess);
        Assert.NotNull(planned);
        Assert.Equal(new[] { 7, 9 }, planned.Keys.Order());
        Assert.All(planned.Values, m => Assert.Contains(nameof(WorkspaceDeletedEvent), m.MessageType));
        _accessTokens.Verify(a => a.RevokeUserAsync(UserRef, It.IsAny<CancellationToken>()), Times.Once);
        // Leaving tenant 3 is announced as the members endpoint would, and the deletion itself.
        Assert.Equal(1, _accessChanges.Announced);
        _bus.Verify(b => b.PublishAsync(It.Is<TenantActorEvent>(e => e is MemberRemovedEvent && ((MemberRemovedEvent)e).MemberRefId == UserRef), It.IsAny<CancellationToken>()), Times.Once);
        _bus.Verify(b => b.PublishAsync(It.Is<AccountDeletedEvent>(e => e.UserRefId == UserRef && e.ClientAddress == Ip), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_deactivated_or_already_deleted_account_cannot_delete()
    {
        var inactive = AUser();
        inactive.IsActive = false;
        _accounts.Setup(p => p.GetByIdAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(inactive);

        var result = await Service().DeleteAccountAsync(42, Password, Ip);

        Assert.Equal("AUTH_USER_NOT_FOUND", result.Error.Code);
        _accounts.Verify(p => p.EndPasswordCheckAsync(It.IsAny<int>(), false, It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
        _accounts.Verify(p => p.DeleteUserAsync(It.IsAny<int>(), It.IsAny<IReadOnlyDictionary<int, OutboxMessage>>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_wrong_password_deletes_nothing_and_counts_towards_the_lock()
    {
        var result = await Service().DeleteAccountAsync(42, "not the password", Ip);

        Assert.Equal("AUTH_CURRENT_PASSWORD_INVALID", result.Error.Code);
        _accounts.Verify(p => p.EndPasswordCheckAsync(42, false, AccountLockout.MaxFailedLogins, AccountLockout.LoginLockout, It.IsAny<CancellationToken>()), Times.Once);
        _accounts.Verify(p => p.DeleteUserAsync(It.IsAny<int>(), It.IsAny<IReadOnlyDictionary<int, OutboxMessage>>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
        _accessTokens.Verify(a => a.RevokeUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_locked_account_is_not_asked_for_its_password()
    {
        var locked = AUser();
        locked.LockedUntil = DateTime.UtcNow.AddMinutes(5);
        _accounts.Setup(p => p.GetByIdAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(locked);

        var result = await Service().DeleteAccountAsync(42, Password, Ip);

        Assert.Equal("AUTH_ACCOUNT_LOCKED", result.Error.Code);
        _accounts.Verify(p => p.DeleteUserAsync(It.IsAny<int>(), It.IsAny<IReadOnlyDictionary<int, OutboxMessage>>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Leaving_a_shared_tenant_nobody_else_administers_is_refused_and_keeps_their_tokens()
    {
        _accounts.Setup(p => p.GetSoleTenantWorkspaceIdsAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<int>());
        _accounts.Setup(p => p.DeleteUserAsync(42, It.IsAny<IReadOnlyDictionary<int, OutboxMessage>>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(50018, "Nobody else administers Acme."));

        var result = await Service().DeleteAccountAsync(42, Password, Ip);

        Assert.Equal("ACCOUNT_SOLE_ADMINISTRATOR", result.Error.Code);
        Assert.Contains("Acme", result.Error.Message);
        _accessTokens.Verify(a => a.RevokeUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_unconfirmed_account_is_deleted_with_its_workspaces_without_asking_the_payment_provider()
    {
        var createdBefore = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        _accounts.Setup(p => p.GetSoleTenantWorkspaceIdsAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(new List<int> { 7 });
        IReadOnlyDictionary<int, OutboxMessage>? planned = null;
        _accounts.Setup(p => p.DeleteUserAsync(42, It.IsAny<IReadOnlyDictionary<int, OutboxMessage>>(), createdBefore, It.IsAny<CancellationToken>()))
            .Callback<int, IReadOnlyDictionary<int, OutboxMessage>, DateTime?, CancellationToken>((_, w, _, _) => planned = w)
            .ReturnsAsync(new List<int>());

        var deleted = await Service().DeleteUnconfirmedAsync(42, createdBefore);

        Assert.True(deleted);
        Assert.Equal([7], planned!.Keys);
        _billing.Verify(b => b.GetLiveSubscriptionsOfSoleTenantsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _bus.Verify(b => b.PublishAsync(It.Is<AccountDeletedEvent>(e => e.UserRefId == UserRef && e.ClientAddress == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_account_confirmed_while_the_sweep_deletes_it_is_kept()
    {
        var createdBefore = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        _accounts.Setup(p => p.GetSoleTenantWorkspaceIdsAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<int>());
        _accounts.Setup(p => p.DeleteUserAsync(42, It.IsAny<IReadOnlyDictionary<int, OutboxMessage>>(), createdBefore, It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(50029, "The account was confirmed or used while it was being deleted as unconfirmed."));

        var deleted = await Service().DeleteUnconfirmedAsync(42, createdBefore);

        Assert.False(deleted);
        _bus.Verify(b => b.PublishAsync(It.IsAny<AccountDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private AccountDeletionService Service() =>
        new(_accounts.Object, _billing.Object, _gateway.Object, _bus.Object, NullLogger<AccountDeletionService>.Instance,
            AuditWorkspaces.None, new TenantAnnouncer(_bus.Object, AuditWorkspaces.None, _accessChanges.Changes, NullLogger<TenantAnnouncer>.Instance),
            CredentialFakes.Credentials(_accounts, Mock.Of<ISessionProvider>(), _bus.Object,
                new SecurityNotices(Mock.Of<IAuthMailer>(), MailCooldownTests.InProcess(), new AuthMetrics(), TimeProvider.System), _accessTokens.Object));

    private static TenantSubscription Subscription(string id) =>
        new(5, "dodo", id, "cus_1", "prod_maker_m", PlanCode.Maker, BillingInterval.Monthly, SubscriptionStatus.Active, DateTime.UtcNow.AddDays(20), false, null, null);

    private static User AUser() => new()
    {
        Id = 42,
        RefId = UserRef,
        Username = "someone",
        Email = "someone@example.test",
        PasswordHash = new PasswordHasher<User>().HashPassword(new User(), Password),
        IsActive = true,
        IsEmailVerified = true
    };
}

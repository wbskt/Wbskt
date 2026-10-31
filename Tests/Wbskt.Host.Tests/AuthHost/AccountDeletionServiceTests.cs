using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure.Events.Outbox;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// Deleting your own account: the password is asked again, the workspaces of tenants only you belong
/// to go with the account, and the access tokens already issued stop working.
/// </summary>
public sealed class AccountDeletionServiceTests
{
    private const string Password = "correct horse battery";
    private const string Ip = "10.0.0.1";
    private static readonly Guid UserRef = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Mock<IAccountProvider> _accounts = new();
    private readonly Mock<IEventBus> _bus = new();
    private readonly Mock<IAccessTokenRevocation> _accessTokens = new();

    public AccountDeletionServiceTests()
    {
        _accounts.Setup(p => p.GetByIdAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(AUser());
    }

    [Fact]
    public async Task Deleting_takes_the_sole_tenant_workspaces_and_revokes_the_access_tokens()
    {
        _accounts.Setup(p => p.GetSoleTenantWorkspaceIdsAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(new List<int> { 7, 9 });
        IReadOnlyDictionary<int, OutboxMessage>? planned = null;
        _accounts.Setup(p => p.DeleteUserAsync(42, It.IsAny<IReadOnlyDictionary<int, OutboxMessage>>(), It.IsAny<CancellationToken>()))
            .Callback<int, IReadOnlyDictionary<int, OutboxMessage>, CancellationToken>((_, w, _) => planned = w)
            .ReturnsAsync(new List<int> { 3 });

        var result = await Service().DeleteAccountAsync(42, Password, Ip);

        Assert.True(result.IsSuccess);
        Assert.NotNull(planned);
        Assert.Equal(new[] { 7, 9 }, planned.Keys.Order());
        Assert.All(planned.Values, m => Assert.Contains(nameof(WorkspaceDeletedEvent), m.MessageType));
        _accessTokens.Verify(a => a.RevokeUserAsync(UserRef, It.IsAny<CancellationToken>()), Times.Once);
        // Leaving tenant 3 is announced as the members endpoint would, and the deletion itself.
        _bus.Verify(b => b.PublishAsync(It.Is<UserPermissionsChangedEvent>(e => e.UserId == 42), It.IsAny<CancellationToken>()), Times.Once);
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
        _accounts.Verify(p => p.RecordLoginFailureAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
        _accounts.Verify(p => p.DeleteUserAsync(It.IsAny<int>(), It.IsAny<IReadOnlyDictionary<int, OutboxMessage>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_wrong_password_deletes_nothing_and_counts_towards_the_lock()
    {
        var result = await Service().DeleteAccountAsync(42, "not the password", Ip);

        Assert.Equal("AUTH_CURRENT_PASSWORD_INVALID", result.Error.Code);
        _accounts.Verify(p => p.RecordLoginFailureAsync(42, AccountLockout.MaxFailedLogins, AccountLockout.LoginLockout, It.IsAny<CancellationToken>()), Times.Once);
        _accounts.Verify(p => p.DeleteUserAsync(It.IsAny<int>(), It.IsAny<IReadOnlyDictionary<int, OutboxMessage>>(), It.IsAny<CancellationToken>()), Times.Never);
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
        _accounts.Verify(p => p.DeleteUserAsync(It.IsAny<int>(), It.IsAny<IReadOnlyDictionary<int, OutboxMessage>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Leaving_a_shared_tenant_nobody_else_administers_is_refused_and_keeps_their_tokens()
    {
        _accounts.Setup(p => p.GetSoleTenantWorkspaceIdsAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<int>());
        _accounts.Setup(p => p.DeleteUserAsync(42, It.IsAny<IReadOnlyDictionary<int, OutboxMessage>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(50018, "Nobody else administers Acme."));

        var result = await Service().DeleteAccountAsync(42, Password, Ip);

        Assert.Equal("ACCOUNT_SOLE_ADMINISTRATOR", result.Error.Code);
        Assert.Contains("Acme", result.Error.Message);
        _accessTokens.Verify(a => a.RevokeUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private AccountDeletionService Service() =>
        new(_accounts.Object, _bus.Object, _accessTokens.Object, NullLogger<AccountDeletionService>.Instance);

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

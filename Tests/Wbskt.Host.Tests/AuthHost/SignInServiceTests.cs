using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Moq;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// The session aggregate: signing in starts a session, and signing out or ending one from the list
/// ends both its refresh token and its access tokens.
/// </summary>
public sealed class SignInServiceTests
{
    private const string Password = "correct horse battery";
    private const string Email = "someone@example.test";
    private const string Ip = "10.0.0.1";
    private static readonly Guid UserRef = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Signing_in_starts_a_session_the_access_token_names()
    {
        var harness = new Harness();
        harness.WithUser(AUser());
        RefreshToken? inserted = null;
        harness.Sessions.Setup(p => p.InsertRefreshTokenAsync(It.IsAny<RefreshToken>(), Ip, It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, string, CancellationToken>((t, _, _) => inserted = t)
            .Returns(Task.CompletedTask);

        var result = await harness.SignIn.LoginAsync(Email, Password, Ip);

        Assert.True(result.IsSuccess);
        Assert.NotNull(inserted);
        Assert.Equal(42, inserted.UserId);
        Assert.Equal(("access-token", inserted.Token), (result.Value.AccessToken, result.Value.RefreshToken));
        var sid = Assert.Single(harness.Claims!, c => c.Type == WbsktClaims.Session);
        Assert.Equal(inserted.SessionId.ToString(), sid.Value);
        harness.Accounts.Verify(p => p.RecordLoginSuccessAsync(42, It.IsAny<CancellationToken>()), Times.Once);
        Assert.IsType<UserLoginSuccessEvent>(Assert.Single(harness.Bus.Published));
    }

    [Fact]
    public async Task An_unknown_address_gets_the_wrong_password_answer()
    {
        var harness = new Harness();
        harness.Accounts.Setup(p => p.GetByEmailAsync(Email, It.IsAny<CancellationToken>())).ThrowsAsync(new SecurityException("No user with that email address."));

        var result = await harness.SignIn.LoginAsync(Email, Password, Ip);

        Assert.Equal("AUTH_INVALID_CREDENTIALS", result.Error.Code);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
        harness.Sessions.Verify(p => p.InsertRefreshTokenAsync(It.IsAny<RefreshToken>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var failed = Assert.IsType<UserLoginFailedEvent>(Assert.Single(harness.Bus.Published));
        Assert.Equal(-1, failed.UserId);
    }

    [Fact]
    public async Task An_inactive_account_starts_no_session()
    {
        var harness = new Harness();
        var user = AUser();
        user.IsActive = false;
        harness.WithUser(user);

        var result = await harness.SignIn.LoginAsync(Email, Password, Ip);

        Assert.Equal("AUTH_USER_INACTIVE", result.Error.Code);
        harness.Sessions.Verify(p => p.InsertRefreshTokenAsync(It.IsAny<RefreshToken>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Accounts.Verify(p => p.RecordLoginSuccessAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_wrong_password_that_locks_the_account_raises_an_alert()
    {
        var harness = new Harness();
        harness.WithUser(AUser());
        harness.Accounts.Setup(p => p.RecordLoginFailureAsync(42, AccountLockout.MaxFailedLogins, AccountLockout.LoginLockout, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DateTime.UtcNow.Add(AccountLockout.LoginLockout));

        var result = await harness.SignIn.LoginAsync(Email, "not the password", Ip);

        Assert.Equal("AUTH_INVALID_CREDENTIALS", result.Error.Code);
        var alert = Assert.Single(harness.Bus.Published.OfType<SecurityAlertEvent>());
        Assert.Equal(("AccountLocked", Ip), (alert.AlertType, alert.IpAddress));
    }

    [Fact]
    public async Task A_wrong_password_short_of_the_limit_raises_no_alert()
    {
        var harness = new Harness();
        harness.WithUser(AUser());
        harness.Accounts.Setup(p => p.RecordLoginFailureAsync(42, It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime?)null);

        await harness.SignIn.LoginAsync(Email, "not the password", Ip);

        Assert.Empty(harness.Bus.Published.OfType<SecurityAlertEvent>());
    }

    [Fact]
    public async Task Signing_out_everywhere_ends_refresh_and_access_tokens()
    {
        var harness = new Harness();
        harness.WithUser(AUser());
        harness.Sessions.Setup(p => p.RevokeAllRefreshTokensForUserAsync(42, Ip, It.IsAny<CancellationToken>())).ReturnsAsync(3);

        var result = await harness.SignIn.LogoutAllAsync(42, Ip);

        Assert.True(result.IsSuccess);
        harness.Sessions.Verify(p => p.RevokeAllRefreshTokensForUserAsync(42, Ip, It.IsAny<CancellationToken>()), Times.Once);
        harness.AccessTokens.Verify(a => a.RevokeUserAsync(UserRef, It.IsAny<CancellationToken>()), Times.Once);
        var revoked = Assert.IsType<SessionsRevokedEvent>(Assert.Single(harness.Bus.Published));
        Assert.Equal(("all", UserRef, Ip), (revoked.Which, revoked.UserRefId, revoked.ClientAddress));
    }

    [Fact]
    public async Task Ending_one_session_is_announced_as_one()
    {
        var harness = new Harness();
        harness.WithUser(AUser());
        var sessionId = Guid.NewGuid();
        harness.Sessions.Setup(p => p.RevokeSessionAsync(sessionId, 42, Ip, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await harness.SignIn.RevokeSessionAsync(42, sessionId, Ip);

        Assert.True(result.IsSuccess);
        harness.AccessTokens.Verify(a => a.RevokeSessionAsync(sessionId, It.IsAny<CancellationToken>()), Times.Once);
        harness.AccessTokens.Verify(a => a.RevokeUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        var revoked = Assert.IsType<SessionsRevokedEvent>(Assert.Single(harness.Bus.Published));
        Assert.Equal("one", revoked.Which);
    }

    private static User AUser() => new()
    {
        Id = 42,
        RefId = UserRef,
        Username = "someone",
        Email = Email,
        PasswordHash = new PasswordHasher<User>().HashPassword(new User(), Password),
        IsActive = true,
        IsEmailVerified = true
    };

    private sealed class Harness : AccountServices
    {
        public Harness() : this(new RecordingBus(), new Mock<IJwtService>())
        {
        }

        private Harness(RecordingBus bus, Mock<IJwtService> jwt) : base(bus: bus, jwt: jwt.Object)
        {
            Bus = bus;
            jwt.Setup(j => j.GenerateToken(It.IsAny<IEnumerable<Claim>>(), It.IsAny<string>(), It.IsAny<TimeSpan>()))
                .Callback<IEnumerable<Claim>, string, TimeSpan>((claims, _, _) => Claims = claims.ToList())
                .Returns("access-token");
        }

        public RecordingBus Bus { get; }

        /// <summary>The claims the last access token was minted with.</summary>
        public List<Claim>? Claims { get; private set; }

        public void WithUser(User user)
        {
            Accounts.Setup(p => p.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>())).ReturnsAsync(user);
            Accounts.Setup(p => p.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        }
    }

    private sealed class RecordingBus : IEventBus
    {
        public List<IEvent> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }
    }
}

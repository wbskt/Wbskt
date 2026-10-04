using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Workflow.Engine.Host.Tests.AuthHost;

/// <summary>
/// Per-account lockout, changing a password while signed in, and ending one session. What matters is
/// that a locked account never gets its password checked, that every wrong password is counted
/// wherever it is typed, and that a password change ends the other sessions without ending this one.
/// </summary>
public sealed class AccountSecurityTests
{
    private const string Password = "correct horse battery";
    private const string Email = "someone@example.test";

    // ---------------------------------------------------------------- lockout on sign-in

    [Fact]
    public async Task A_locked_account_is_refused_even_with_the_right_password()
    {
        var harness = new Harness();
        harness.WithUser(AUser(lockedUntil: DateTime.UtcNow.AddMinutes(5)));

        var result = await harness.Service.LoginAsync(Email, Password, "10.0.0.1");

        Assert.True(result.IsFailure);
        // The ordinary answer, so a lock says nothing about whether the address has an account.
        Assert.Equal("AUTH_INVALID_CREDENTIALS", result.Error.Code);
        harness.Provider.Verify(p => p.RecordLoginSuccessAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Provider.Verify(p => p.RecordLoginFailureAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_lapsed_lock_does_not_stop_sign_in()
    {
        var harness = new Harness();
        harness.WithUser(AUser(lockedUntil: DateTime.UtcNow.AddMinutes(-1)));

        var result = await harness.Service.LoginAsync(Email, Password, "10.0.0.1");

        Assert.True(result.IsSuccess);
        harness.Provider.Verify(p => p.RecordLoginSuccessAsync(42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_wrong_password_is_counted_against_the_account()
    {
        var harness = new Harness();
        harness.WithUser(AUser());

        var result = await harness.Service.LoginAsync(Email, "not the password", "10.0.0.1");

        Assert.Equal("AUTH_INVALID_CREDENTIALS", result.Error.Code);
        harness.Provider.Verify(p => p.RecordLoginFailureAsync(42, AuthService.MaxFailedLogins, AuthService.LoginLockout, It.IsAny<CancellationToken>()), Times.Once);
        harness.Provider.Verify(p => p.RecordLoginSuccessAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------- rehash on sign-in

    [Fact]
    public async Task A_hash_from_older_settings_is_upgraded_on_sign_in()
    {
        var harness = new Harness();
        var weak = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { IterationCount = 1_000 }));
        var user = AUser();
        user.PasswordHash = weak.HashPassword(new User(), Password);
        harness.WithUser(user);
        string? newHash = null;
        harness.Provider
            .Setup(p => p.UpgradePasswordHashAsync(42, user.PasswordHash, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<int, string, string, CancellationToken>((_, _, hash, _) => newHash = hash)
            .ReturnsAsync(true);

        var result = await harness.Service.LoginAsync(Email, Password, "10.0.0.1");

        Assert.True(result.IsSuccess);
        Assert.NotNull(newHash);
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<User>().VerifyHashedPassword(new User(), newHash!, Password));
    }

    [Fact]
    public async Task A_current_hash_is_left_alone()
    {
        var harness = new Harness();
        harness.WithUser(AUser());

        await harness.Service.LoginAsync(Email, Password, "10.0.0.1");

        harness.Provider.Verify(p => p.UpgradePasswordHashAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_failed_upgrade_does_not_fail_the_sign_in()
    {
        var harness = new Harness();
        var user = AUser();
        user.PasswordHash = new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { IterationCount = 1_000 })).HashPassword(new User(), Password);
        harness.WithUser(user);
        harness.Provider
            .Setup(p => p.UpgradePasswordHashAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException());

        var result = await harness.Service.LoginAsync(Email, Password, "10.0.0.1");

        Assert.True(result.IsSuccess);
    }

    // ---------------------------------------------------------------- change password

    [Fact]
    public async Task Changing_the_password_ends_every_session_and_returns_a_new_one()
    {
        var harness = new Harness();
        harness.WithUser(AUser());
        string? storedHash = null;
        harness.Provider
            .Setup(p => p.ChangePasswordAsync(42, It.IsAny<string>(), "10.0.0.1", It.IsAny<CancellationToken>()))
            .Callback<int, string, string?, CancellationToken>((_, hash, _, _) => storedHash = hash)
            .Returns(Task.CompletedTask);

        var result = await harness.Service.ChangePasswordAsync(42, Password, "a brand new passphrase", "10.0.0.1");

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrEmpty(result.Value.RefreshToken));
        Assert.NotNull(storedHash);
        Assert.NotEqual(
            PasswordVerificationResult.Failed,
            new PasswordHasher<User>().VerifyHashedPassword(new User(), storedHash!, "a brand new passphrase"));
        harness.AccessTokens.Verify(a => a.RevokeUserAsync(42, It.IsAny<CancellationToken>()), Times.Once);
        harness.Provider.Verify(p => p.InsertRefreshTokenAsync(It.Is<RefreshToken>(t => t.UserId == 42), "10.0.0.1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_wrong_current_password_changes_nothing_and_is_counted()
    {
        var harness = new Harness();
        harness.WithUser(AUser());

        var result = await harness.Service.ChangePasswordAsync(42, "not the password", "a brand new passphrase", "10.0.0.1");

        Assert.True(result.IsFailure);
        Assert.Equal("AUTH_CURRENT_PASSWORD_INVALID", result.Error.Code);
        // Not 401: the session is fine, and a client would sign the user out on one.
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        harness.Provider.Verify(p => p.ChangePasswordAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Provider.Verify(p => p.RecordLoginFailureAsync(42, AuthService.MaxFailedLogins, AuthService.LoginLockout, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_locked_account_cannot_change_its_password()
    {
        var harness = new Harness();
        harness.WithUser(AUser(lockedUntil: DateTime.UtcNow.AddMinutes(5)));

        var result = await harness.Service.ChangePasswordAsync(42, Password, "a brand new passphrase", "10.0.0.1");

        Assert.Equal("AUTH_ACCOUNT_LOCKED", result.Error.Code);
        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
        harness.Provider.Verify(p => p.ChangePasswordAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------- sessions

    private static readonly Guid SessionId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task Ending_a_session_that_is_not_yours_reads_as_not_found()
    {
        var harness = new Harness();
        harness.Provider
            .Setup(p => p.RevokeSessionAsync(SessionId, 42, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await harness.Service.RevokeSessionAsync(42, SessionId, "10.0.0.1");

        Assert.Equal("AUTH_SESSION_NOT_FOUND", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        harness.AccessTokens.Verify(a => a.RevokeSessionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The access token goes too. Without it, a session ended from the list kept working on that
    /// device until its access token ran out.
    /// </summary>
    [Fact]
    public async Task Ending_your_own_session_ends_its_access_token_too()
    {
        var harness = new Harness();
        harness.Provider
            .Setup(p => p.RevokeSessionAsync(SessionId, 42, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await harness.Service.RevokeSessionAsync(42, SessionId, "10.0.0.1");

        Assert.True(result.IsSuccess);
        harness.AccessTokens.Verify(a => a.RevokeSessionAsync(SessionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Logging_out_ends_the_sessions_access_token()
    {
        var harness = new Harness();
        harness.Provider
            .Setup(p => p.RevokeRefreshTokenAsync("refresh", It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SessionId);

        var result = await harness.Service.LogoutAsync("refresh", "10.0.0.1");

        Assert.True(result.IsSuccess);
        harness.AccessTokens.Verify(a => a.RevokeSessionAsync(SessionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Logging_out_with_a_dead_token_ends_nothing_and_still_succeeds()
    {
        var harness = new Harness();
        harness.Provider
            .Setup(p => p.RevokeRefreshTokenAsync(It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var result = await harness.Service.LogoutAsync("dead", "10.0.0.1");

        Assert.True(result.IsSuccess);
        harness.AccessTokens.Verify(a => a.RevokeSessionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_session_list_marks_the_one_the_request_came_from()
    {
        var harness = new Harness();
        var other = Guid.NewGuid();
        var now = DateTime.UtcNow;
        harness.Provider
            .Setup(p => p.GetActiveSessionsAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SessionResponse(other, now, now, now.AddDays(7), null), new SessionResponse(SessionId, now, now, now.AddDays(7), null)]);

        var result = await harness.Service.GetSessionsAsync(42, SessionId);

        Assert.Equal([false, true], result.Value.Select(s => s.IsCurrent));
    }

    private static User AUser(DateTime? lockedUntil = null) => new()
    {
        Id = 42,
        RefId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Username = "someone",
        Email = Email,
        PasswordHash = new PasswordHasher<User>().HashPassword(new User(), Password),
        IsActive = true,
        IsEmailVerified = true,
        LockedUntil = lockedUntil
    };

    private sealed class Harness
    {
        public Harness()
        {
            var jwt = new Mock<IJwtService>();
            jwt.Setup(j => j.GenerateToken(It.IsAny<IEnumerable<Claim>>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).Returns("access-token");

            Service = new AuthService(
                Provider.Object,
                jwt.Object,
                Mock.Of<IEventBus>(),
                Mock.Of<IAuthMailer>(),
                Options.Create(new AuthEmailOptions()),
                NullLogger<AuthService>.Instance,
                new AuthMetrics(),
                AccessTokens.Object,
                Options.Create(new AccessTokenOptions()),
                MailCooldownTests.InProcess());
        }

        public Mock<IAuthProvider> Provider { get; } = new();

        public Mock<IAccessTokenRevocation> AccessTokens { get; } = new();

        public AuthService Service { get; }

        public void WithUser(User user)
        {
            Provider.Setup(p => p.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>())).ReturnsAsync(user);
            Provider.Setup(p => p.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        }
    }
}

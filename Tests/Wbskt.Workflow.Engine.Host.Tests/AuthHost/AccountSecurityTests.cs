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

    [Fact]
    public async Task Ending_a_session_that_is_not_yours_reads_as_not_found()
    {
        var harness = new Harness();
        harness.Provider
            .Setup(p => p.RevokeSessionAsync(7, 42, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await harness.Service.RevokeSessionAsync(42, 7, "10.0.0.1");

        Assert.Equal("AUTH_SESSION_NOT_FOUND", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task Ending_your_own_session_succeeds()
    {
        var harness = new Harness();
        harness.Provider
            .Setup(p => p.RevokeSessionAsync(7, 42, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await harness.Service.RevokeSessionAsync(42, 7, "10.0.0.1");

        Assert.True(result.IsSuccess);
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
                Options.Create(new AccessTokenOptions()));
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

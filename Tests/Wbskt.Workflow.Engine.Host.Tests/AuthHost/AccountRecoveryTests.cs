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
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Workflow.Engine.Host.Tests.AuthHost;

/// <summary>
/// The two properties this feature exists to have, and neither of which is visible from a happy-path
/// test: sign-in refuses an address nobody has proved they control, and none of the recovery
/// endpoints reveals whether an address is registered.
/// </summary>
public sealed class AccountRecoveryTests
{
    private const string Password = "correct horse battery";

    // ---------------------------------------------------------------- sign-in gate

    [Fact]
    public async Task Login_is_refused_while_the_address_is_unverified()
    {
        var harness = new Harness();
        harness.WithUser(Verified(false));

        var result = await harness.Service.LoginAsync("someone@example.test", Password, "10.0.0.1");

        Assert.True(result.IsFailure);
        Assert.Equal("AUTH_EMAIL_UNVERIFIED", result.Error.Code);
    }

    [Fact]
    public async Task Login_succeeds_once_the_address_is_verified()
    {
        var harness = new Harness();
        harness.WithUser(Verified(true));

        var result = await harness.Service.LoginAsync("someone@example.test", Password, "10.0.0.1");

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// The unverified answer must sit behind the password check. In front of it, it would confirm to
    /// anyone who asked that an address has an account — the thing every other endpoint here is
    /// careful not to say.
    /// </summary>
    [Fact]
    public async Task Login_with_a_wrong_password_says_nothing_about_verification()
    {
        var harness = new Harness();
        harness.WithUser(Verified(false));

        var result = await harness.Service.LoginAsync("someone@example.test", "not the password", "10.0.0.1");

        Assert.True(result.IsFailure);
        Assert.Equal("AUTH_INVALID_CREDENTIALS", result.Error.Code);
    }

    /// <summary>
    /// The escape hatch the end-to-end suite depends on. Pinned so that a change to the default, or
    /// to which way the flag reads, fails here rather than silently either bricking that suite or —
    /// far worse — disabling the check in a deployment.
    /// </summary>
    [Fact]
    public async Task Login_admits_an_unverified_address_only_when_the_check_is_switched_off()
    {
        var harness = new Harness(requireVerifiedEmail: false);
        harness.WithUser(Verified(false));

        var result = await harness.Service.LoginAsync("someone@example.test", Password, "10.0.0.1");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void The_check_is_on_unless_configuration_says_otherwise()
    {
        Assert.True(new AuthEmailOptions().RequireVerifiedEmailForSignIn);
    }

    // ---------------------------------------------------------------- forgot password

    [Fact]
    public async Task ForgotPassword_issues_a_token_and_mails_the_owner()
    {
        var harness = new Harness();
        harness.WithUser(Verified(true));

        var result = await harness.Service.ForgotPasswordAsync("someone@example.test", "10.0.0.1");

        Assert.True(result.IsSuccess);
        harness.Provider.Verify(p => p.CreatePasswordResetTokenAsync(42, It.IsAny<byte[]>(), It.IsAny<DateTime>(), "10.0.0.1", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(["reset:someone@example.test"], harness.Mailer.Sent);
    }

    [Fact]
    public async Task ForgotPassword_answers_identically_for_an_address_with_no_account()
    {
        var harness = new Harness();
        harness.WithNoUser();

        var result = await harness.Service.ForgotPasswordAsync("nobody@example.test", "10.0.0.1");

        // Same success the real address gets, and nothing done that an observer could detect.
        Assert.True(result.IsSuccess);
        harness.Provider.Verify(p => p.CreatePasswordResetTokenAsync(It.IsAny<int>(), It.IsAny<byte[]>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(harness.Mailer.Sent);
    }

    /// <summary>
    /// Reactivating an account is an administrator's decision, so its former owner must not be able
    /// to recover it — and must not be able to learn that it exists by trying.
    /// </summary>
    [Fact]
    public async Task ForgotPassword_does_not_recover_a_deactivated_account()
    {
        var harness = new Harness();
        harness.WithUser(Verified(true, isActive: false));

        var result = await harness.Service.ForgotPasswordAsync("someone@example.test", "10.0.0.1");

        Assert.True(result.IsSuccess);
        Assert.Empty(harness.Mailer.Sent);
    }

    /// <summary>A relay that is down must not turn into a way to probe which addresses are real.</summary>
    [Fact]
    public async Task ForgotPassword_still_succeeds_when_the_token_cannot_be_stored()
    {
        var harness = new Harness();
        harness.WithUser(Verified(true));
        harness.Provider
            .Setup(p => p.CreatePasswordResetTokenAsync(It.IsAny<int>(), It.IsAny<byte[]>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database is unreachable"));

        var result = await harness.Service.ForgotPasswordAsync("someone@example.test", "10.0.0.1");

        Assert.True(result.IsSuccess);
    }

    // ---------------------------------------------------------------- reset

    [Fact]
    public async Task ResetPassword_rejects_a_token_that_is_not_valid()
    {
        var harness = new Harness();
        harness.Provider
            .Setup(p => p.ConsumePasswordResetTokenAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SecurityException("nope"));

        var result = await harness.Service.ResetPasswordAsync("whatever", "a new long password", "10.0.0.1");

        Assert.True(result.IsFailure);
        Assert.Equal("RESET_TOKEN_INVALID", result.Error.Code);
    }

    /// <summary>
    /// The new password reaches the provider already hashed. A reset that stored the plaintext would
    /// pass every functional test in this file.
    /// </summary>
    [Fact]
    public async Task ResetPassword_hashes_the_password_before_it_leaves_the_service()
    {
        var harness = new Harness();
        string? stored = null;
        harness.Provider
            .Setup(p => p.ConsumePasswordResetTokenAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<byte[], string, string?, CancellationToken>((_, hash, _, _) => stored = hash)
            .ReturnsAsync(42);

        var result = await harness.Service.ResetPasswordAsync("token", "a new long password", "10.0.0.1");

        Assert.True(result.IsSuccess);
        Assert.NotNull(stored);
        Assert.NotEqual("a new long password", stored);
        Assert.Equal(PasswordVerificationResult.Success, new PasswordHasher<User>().VerifyHashedPassword(new User(), stored!, "a new long password"));
    }

    /// <summary>
    /// Revocation is not a second call the service makes and could skip — it happens inside the same
    /// transaction that writes the password. This pins that it is not quietly moved back out.
    /// </summary>
    [Fact]
    public async Task ResetPassword_does_not_revoke_sessions_in_a_separate_call()
    {
        var harness = new Harness();
        harness.Provider
            .Setup(p => p.ConsumePasswordResetTokenAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);

        await harness.Service.ResetPasswordAsync("token", "a new long password", "10.0.0.1");

        harness.Provider.Verify(p => p.RevokeAllRefreshTokensForUserAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------- verification

    [Fact]
    public async Task VerifyEmail_rejects_a_token_that_is_not_valid()
    {
        var harness = new Harness();
        harness.Provider
            .Setup(p => p.ConsumeEmailVerificationTokenAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SecurityException("nope"));

        var result = await harness.Service.VerifyEmailAsync("whatever");

        Assert.True(result.IsFailure);
        Assert.Equal("VERIFICATION_TOKEN_INVALID", result.Error.Code);
    }

    [Fact]
    public async Task VerifyEmail_consumes_the_token_and_reports_success()
    {
        var harness = new Harness();
        harness.Provider
            .Setup(p => p.ConsumeEmailVerificationTokenAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);

        var result = await harness.Service.VerifyEmailAsync("a-token");

        Assert.True(result.IsSuccess);
        harness.Provider.Verify(p => p.ConsumeEmailVerificationTokenAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The token reaches the provider as a hash, never as the string the user pasted in. A provider
    /// that logged its arguments must not be able to leak a usable credential.
    /// </summary>
    [Fact]
    public async Task VerifyEmail_hashes_the_token_before_it_leaves_the_service()
    {
        var harness = new Harness();
        byte[]? seen = null;
        harness.Provider
            .Setup(p => p.ConsumeEmailVerificationTokenAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Callback<byte[], CancellationToken>((hash, _) => seen = hash)
            .ReturnsAsync(42);

        await harness.Service.VerifyEmailAsync("a-token");

        Assert.NotNull(seen);
        Assert.Equal(32, seen!.Length);
        Assert.Equal(SecurityTokens.Hash("a-token"), seen);
    }

    [Fact]
    public async Task ResendVerification_reissues_for_an_unverified_account()
    {
        var harness = new Harness();
        harness.WithUser(Verified(false));

        var result = await harness.Service.ResendVerificationAsync("someone@example.test");

        Assert.True(result.IsSuccess);
        Assert.Equal(["verify:someone@example.test"], harness.Mailer.Sent);
    }

    [Fact]
    public async Task ResendVerification_sends_nothing_to_an_account_that_is_already_verified()
    {
        var harness = new Harness();
        harness.WithUser(Verified(true));

        var result = await harness.Service.ResendVerificationAsync("someone@example.test");

        Assert.True(result.IsSuccess);
        Assert.Empty(harness.Mailer.Sent);
    }

    [Fact]
    public async Task ResendVerification_answers_identically_for_an_address_with_no_account()
    {
        var harness = new Harness();
        harness.WithNoUser();

        var result = await harness.Service.ResendVerificationAsync("nobody@example.test");

        Assert.True(result.IsSuccess);
        Assert.Empty(harness.Mailer.Sent);
    }

    // ---------------------------------------------------------------- registration

    /// <summary>
    /// A new account is unusable until its address is confirmed, so registration that did not issue
    /// the link would create accounts nobody could ever sign in to.
    /// </summary>
    [Fact]
    public async Task Registration_issues_a_verification_link()
    {
        var harness = new Harness();
        harness.WithSuccessfulRegistration();

        var result = await harness.Service.RegisterUserAsync("someone", "someone@example.test", "a perfectly long password");

        Assert.True(result.IsSuccess);
        harness.Provider.Verify(p => p.CreateEmailVerificationTokenAsync(42, It.IsAny<byte[]>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(["verify:someone@example.test"], harness.Mailer.Sent);
    }

    /// <summary>
    /// Long enough to survive someone signing up in the evening and reading their mail the next
    /// morning. A reset link is short-lived because it is a credential for an existing account; this
    /// one only ever turns an unusable account into a usable one.
    /// </summary>
    [Fact]
    public async Task A_verification_link_outlives_a_night()
    {
        var harness = new Harness();
        harness.WithSuccessfulRegistration();
        DateTime? expiry = null;
        harness.Provider
            .Setup(p => p.CreateEmailVerificationTokenAsync(It.IsAny<int>(), It.IsAny<byte[]>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<int, byte[], DateTime, CancellationToken>((_, _, e, _) => expiry = e)
            .Returns(Task.CompletedTask);

        await harness.Service.RegisterUserAsync("someone", "someone@example.test", "a perfectly long password");

        Assert.NotNull(expiry);
        Assert.True(expiry!.Value - DateTime.UtcNow > TimeSpan.FromHours(12));
    }

    /// <summary>A reset link, by contrast, must not sit live in an unattended inbox for long.</summary>
    [Fact]
    public async Task A_reset_link_is_short_lived()
    {
        var harness = new Harness();
        harness.WithUser(Verified(true));
        DateTime? expiry = null;
        harness.Provider
            .Setup(p => p.CreatePasswordResetTokenAsync(It.IsAny<int>(), It.IsAny<byte[]>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<int, byte[], DateTime, string?, CancellationToken>((_, _, e, _, _) => expiry = e)
            .Returns(Task.CompletedTask);

        await harness.Service.ForgotPasswordAsync("someone@example.test", "10.0.0.1");

        Assert.NotNull(expiry);
        Assert.True(expiry!.Value - DateTime.UtcNow <= TimeSpan.FromHours(1));
    }

    // ---------------------------------------------------------------- support

    private static User Verified(bool isEmailVerified, bool isActive = true) => new()
    {
        Id = 42,
        RefId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Username = "someone",
        Email = "someone@example.test",
        PasswordHash = new PasswordHasher<User>().HashPassword(new User(), Password),
        IsActive = isActive,
        IsEmailVerified = isEmailVerified
    };

    private sealed class Harness
    {
        public Harness(bool requireVerifiedEmail = true)
        {
            var jwt = new Mock<IJwtService>();
            jwt.Setup(j => j.GenerateToken(It.IsAny<IEnumerable<Claim>>(), It.IsAny<TimeSpan>())).Returns("access-token");

            Service = new AuthService(
                Provider.Object,
                jwt.Object,
                Mock.Of<IEventBus>(),
                Mailer,
                Options.Create(new AuthEmailOptions { RequireVerifiedEmailForSignIn = requireVerifiedEmail }),
                NullLogger<AuthService>.Instance,
                new AuthMetrics());
        }

        public Mock<IAuthProvider> Provider { get; } = new();

        public RecordingMailer Mailer { get; } = new();

        public AuthService Service { get; }

        public void WithUser(User user) =>
            Provider.Setup(p => p.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        /// <summary>
        /// The provider throws for an unknown address rather than returning null — that is the shape
        /// the real one has, and the difference is what every non-disclosure test here turns on.
        /// </summary>
        /// <summary>Enough of the provider stubbed for RegisterUserAsync to reach its success path.</summary>
        public void WithSuccessfulRegistration()
        {
            var created = new User
            {
                Id = 42,
                RefId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Username = "someone",
                Email = "someone@example.test",
                IsActive = true
            };

            Provider.Setup(p => p.InsertUserAsync(It.IsAny<User>(), It.IsAny<CancellationToken>())).ReturnsAsync(42);
            Provider.Setup(p => p.CreateTenantAsync(It.IsAny<string>(), It.IsAny<string>(), 42, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Guid.NewGuid());
            Provider.Setup(p => p.GetByIdAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(created);
        }

        public void WithNoUser() =>
            Provider.Setup(p => p.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SecurityException("User not found."));
    }

    /// <summary>
    /// Records what was queued, not what was sent — the real mailer hands off to a background
    /// dispatcher, and every assertion in this file is about the decision to send, not the relay.
    /// Tokens are deliberately not captured: nothing in the service should be handing them anywhere
    /// a test could reach them except here, and asserting on one would make that feel normal.
    /// </summary>
    private sealed class RecordingMailer : IAuthMailer
    {
        public List<string> Sent { get; } = [];

        public bool IsConfigured => true;

        public ValueTask QueueInvitationAsync(string to, string tenantName, string token, DateTime expiresAtUtc, CancellationToken ct)
        {
            Sent.Add($"invite:{to}");
            return ValueTask.CompletedTask;
        }

        public ValueTask QueueEmailVerificationAsync(string to, string username, string token, DateTime expiresAtUtc, CancellationToken ct)
        {
            Sent.Add($"verify:{to}");
            return ValueTask.CompletedTask;
        }

        public ValueTask QueuePasswordResetAsync(string to, string username, string token, DateTime expiresAtUtc, CancellationToken ct)
        {
            Sent.Add($"reset:{to}");
            return ValueTask.CompletedTask;
        }

        public ValueTask QueueAccountAlreadyExistsAsync(string to, string username, CancellationToken ct)
        {
            Sent.Add($"exists:{to}");
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// Registration answers the same way whether or not an address is already taken. The only thing that
/// differs is who gets mailed, and that goes to the address itself rather than to whoever submitted
/// the form.
/// </summary>
public sealed class RegistrationDisclosureTests
{
    [Fact]
    public async Task An_address_that_already_has_an_account_succeeds_and_the_owner_is_told()
    {
        var harness = new ConflictHarness();
        harness.WithExistingAccount();

        var result = await harness.Service.HandleRegistrationConflictAsync("newname", "taken@example.test", new InvalidOperationException("unique index"), CancellationToken.None);

        // The same 204 a real registration produces. Nothing about the existing account leaks back.
        Assert.True(result.IsSuccess);
        Assert.Equal(["exists:taken@example.test"], harness.Mailer.Sent);
    }

    /// <summary>
    /// A username collision says nothing about any address, and answering 204 would leave someone
    /// retrying a name that can never be accepted.
    /// </summary>
    [Fact]
    public async Task A_username_that_is_taken_is_reported_plainly()
    {
        var harness = new ConflictHarness();
        harness.WithNoAccountOnThatAddress();

        var result = await harness.Service.HandleRegistrationConflictAsync("taken", "fresh@example.test", new InvalidOperationException("unique index"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("AUTH_USERNAME_CONFLICT", result.Error.Code);
        Assert.Empty(harness.Mailer.Sent);
    }

    [Fact]
    public async Task A_relay_failure_does_not_turn_a_taken_address_into_a_visible_error()
    {
        var harness = new ConflictHarness(mailerThrows: true);
        harness.WithExistingAccount();

        var result = await harness.Service.HandleRegistrationConflictAsync("newname", "taken@example.test", new InvalidOperationException("unique index"), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    private sealed class ConflictHarness
    {
        public ConflictHarness(bool mailerThrows = false)
        {
            Mailer = new ThrowingMailer(mailerThrows);

            Service = new AuthService(
                Provider.Object,
                Mock.Of<IJwtService>(),
                Mock.Of<IEventBus>(),
                Mailer,
                Options.Create(new AuthEmailOptions()),
                NullLogger<AuthService>.Instance,
                new AuthMetrics());
        }

        public Mock<IAuthProvider> Provider { get; } = new();

        public ThrowingMailer Mailer { get; }

        public AuthService Service { get; }

        public void WithExistingAccount() =>
            Provider.Setup(p => p.GetByEmailAsync("taken@example.test", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new User { Id = 7, Username = "original", Email = "taken@example.test", IsActive = true, IsEmailVerified = true });

        public void WithNoAccountOnThatAddress() =>
            Provider.Setup(p => p.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SecurityException("User not found."));
    }

    private sealed class ThrowingMailer(bool shouldThrow) : IAuthMailer
    {
        public List<string> Sent { get; } = [];

        public bool IsConfigured => true;

        public ValueTask QueueInvitationAsync(string to, string tenantName, string token, DateTime expiresAtUtc, CancellationToken ct) => ValueTask.CompletedTask;

        public ValueTask QueueEmailVerificationAsync(string to, string username, string token, DateTime expiresAtUtc, CancellationToken ct) => ValueTask.CompletedTask;

        public ValueTask QueuePasswordResetAsync(string to, string username, string token, DateTime expiresAtUtc, CancellationToken ct) => ValueTask.CompletedTask;

        public ValueTask QueueAccountAlreadyExistsAsync(string to, string username, CancellationToken ct)
        {
            if (shouldThrow)
            {
                throw new IOException("relay closed the connection");
            }

            Sent.Add($"exists:{to}");
            return ValueTask.CompletedTask;
        }
    }
}

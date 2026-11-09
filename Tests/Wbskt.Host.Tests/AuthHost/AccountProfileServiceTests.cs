using Microsoft.AspNetCore.Identity;
using Moq;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// Changing the username and the email address (#151). The address does not move until the new one
/// is proved, the old one can undo it for a week, and nothing here says whose an address is.
/// </summary>
public sealed class AccountProfileServiceTests
{
    private const string Password = "correct horse battery";
    private const string Email = "someone@example.test";
    private const string NewEmail = "someone.else@example.test";
    private static readonly Guid UserRef = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SessionId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    // ---------------------------------------------------------------- reserved names

    [Theory]
    [InlineData("admin")]
    [InlineData("Admin_")]
    [InlineData("ADM1N")]
    [InlineData("root2")]
    [InlineData("r00t")]
    [InlineData("the.support.team")]
    [InlineData("System")]
    [InlineData("wbskt-help")]
    [InlineData("MyWbsktBot")]
    [InlineData("administrator")]
    public void Staff_names_and_their_look_alikes_are_reserved(string name) =>
        Assert.True(ReservedUsernames.IsReserved(name));

    [Theory]
    [InlineData("ada")]
    [InlineData("rootbeer")]
    [InlineData("systematic")]
    [InlineData("helper")]
    [InlineData("adminton")]
    [InlineData("日本語ユーザー")]
    public void Ordinary_names_are_not_reserved(string name) =>
        Assert.False(ReservedUsernames.IsReserved(name));

    [Fact]
    public async Task Sign_up_refuses_a_reserved_name_too()
    {
        var harness = new Harness();

        var result = await harness.Registration.RegisterUserAsync("Support", "new@example.test", Password);

        Assert.Equal("USERNAME_RESERVED", result.Error.Code);
        harness.Accounts.Verify(p => p.InsertUserAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------- username

    [Fact]
    public async Task A_rename_returns_a_token_for_this_session_and_reaches_the_audit_log()
    {
        var harness = new Harness();
        var user = harness.WithUser();
        harness.Accounts
            .Setup(p => p.ChangeUsernameAsync(42, "ada", AccountProfileService.UsernameChangeInterval, It.IsAny<CancellationToken>()))
            .Callback(() => harness.WithUser(username: "ada"))
            .Returns(Task.CompletedTask);

        var result = await harness.Profile.ChangeUsernameAsync(42, SessionId, "ada", "10.0.0.1");

        Assert.True(result.IsSuccess);
        Assert.Equal("access-token", result.Value.AccessToken);
        Assert.Null(result.Value.RefreshToken);
        var changed = Assert.Single(harness.Published.OfType<UserProfileChangedEvent>());
        Assert.Equal(("username", user.Username, "ada", "changed"), (changed.Field, changed.OldValue, changed.NewValue, changed.How));
        Assert.Equal([10], changed.WorkspaceIds);
        harness.Sessions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_reserved_name_is_refused_before_the_database()
    {
        var harness = new Harness();
        harness.WithUser();

        var result = await harness.Profile.ChangeUsernameAsync(42, SessionId, "Adm1n", "10.0.0.1");

        Assert.Equal("USERNAME_RESERVED", result.Error.Code);
        harness.Accounts.Verify(p => p.ChangeUsernameAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(2627, "USERNAME_TAKEN", ErrorType.Conflict)]
    [InlineData(50022, "USERNAME_CHANGED_RECENTLY", ErrorType.Validation)]
    public async Task A_taken_name_or_a_second_rename_within_30_days_is_refused(int sqlError, string code, ErrorType type)
    {
        var harness = new Harness();
        harness.WithUser();
        harness.Accounts
            .Setup(p => p.ChangeUsernameAsync(42, "ada", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(SqlExceptions.WithNumber(sqlError, "It can be changed again after 2026-12-01T00:00:00 UTC."));

        var result = await harness.Profile.ChangeUsernameAsync(42, SessionId, "ada", "10.0.0.1");

        Assert.Equal(code, result.Error.Code);
        Assert.Equal(type, result.Error.Type);
        Assert.Empty(harness.Published.OfType<UserProfileChangedEvent>());
    }

    // ---------------------------------------------------------------- email: request

    [Fact]
    public async Task A_change_mails_the_new_address_and_changes_nothing_yet()
    {
        var harness = new Harness();
        harness.WithUser();

        var result = await harness.Profile.RequestEmailChangeAsync(42, Password, NewEmail, "10.0.0.1");

        Assert.True(result.IsSuccess);
        Assert.Equal([$"change:{NewEmail}"], harness.Mailer.Sent);
        harness.Accounts.Verify(p => p.CreateEmailChangeTokenAsync(42, NewEmail, It.IsAny<byte[]>(), It.IsAny<DateTime>(), "10.0.0.1", It.IsAny<CancellationToken>()), Times.Once);
        harness.AccessTokens.VerifyNoOtherCalls();
        Assert.Empty(harness.Published.OfType<UserProfileChangedEvent>());
    }

    [Fact]
    public async Task A_wrong_password_starts_nothing_and_is_counted()
    {
        var harness = new Harness();
        harness.WithUser();

        var result = await harness.Profile.RequestEmailChangeAsync(42, "not the password", NewEmail, "10.0.0.1");

        Assert.Equal("AUTH_CURRENT_PASSWORD_INVALID", result.Error.Code);
        harness.Accounts.Verify(p => p.RecordLoginFailureAsync(42, AccountLockout.MaxFailedLogins, AccountLockout.LoginLockout, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(harness.Mailer.Sent);
    }

    [Fact]
    public async Task A_locked_account_is_not_asked_for_its_password()
    {
        var harness = new Harness();
        harness.WithUser(lockedUntil: DateTime.UtcNow.AddMinutes(5));

        var result = await harness.Profile.RequestEmailChangeAsync(42, Password, NewEmail, "10.0.0.1");

        Assert.Equal("AUTH_ACCOUNT_LOCKED", result.Error.Code);
        Assert.Empty(harness.Mailer.Sent);
    }

    [Fact]
    public async Task An_address_another_account_uses_gets_the_generic_conflict_and_no_mail()
    {
        var harness = new Harness();
        harness.WithUser();
        harness.Accounts.Setup(p => p.GetByEmailAsync(NewEmail, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = 7, Email = NewEmail });

        var result = await harness.Profile.RequestEmailChangeAsync(42, Password, NewEmail, "10.0.0.1");

        Assert.Equal("EMAIL_CHANGE_CONFLICT", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(harness.Mailer.Sent);
    }

    [Fact]
    public async Task The_current_address_is_not_a_change()
    {
        var harness = new Harness();
        harness.WithUser();

        var result = await harness.Profile.RequestEmailChangeAsync(42, Password, Email.ToUpperInvariant(), "10.0.0.1");

        Assert.Equal("EMAIL_UNCHANGED", result.Error.Code);
    }

    [Fact]
    public async Task Asking_again_straight_away_keeps_the_first_link_and_sends_nothing()
    {
        var harness = new Harness();
        harness.WithUser();

        await harness.Profile.RequestEmailChangeAsync(42, Password, NewEmail, "10.0.0.1");
        var again = await harness.Profile.RequestEmailChangeAsync(42, Password, NewEmail, "10.0.0.1");

        Assert.True(again.IsSuccess);
        Assert.Single(harness.Mailer.Sent);
        harness.Accounts.Verify(p => p.CreateEmailChangeTokenAsync(42, NewEmail, It.IsAny<byte[]>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------------------------------------------------------- email: confirm and revert

    [Fact]
    public async Task Confirming_switches_ends_every_session_and_mails_the_old_address_an_undo()
    {
        var harness = new Harness();
        harness.WithUser();
        DateTime revertExpiry = default;
        harness.Accounts
            .Setup(p => p.ConfirmEmailChangeAsync(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<DateTime>(), "10.0.0.1", It.IsAny<CancellationToken>()))
            .Callback<byte[], byte[], DateTime, string?, CancellationToken>((_, _, expires, _, _) => revertExpiry = expires)
            .ReturnsAsync(new EmailChange(42, Email, NewEmail));

        var result = await harness.Profile.ConfirmEmailChangeAsync("token", callerUserId: null, "10.0.0.1");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal([$"changed:{Email}->{NewEmail}"], harness.Mailer.Sent);
        Assert.InRange(revertExpiry, DateTime.UtcNow.AddDays(6.9), DateTime.UtcNow.AddDays(7.1));
        harness.AccessTokens.Verify(a => a.RevokeUserAsync(UserRef, It.IsAny<CancellationToken>()), Times.Once);
        var changed = Assert.Single(harness.Published.OfType<UserProfileChangedEvent>());
        Assert.Equal(("email", Email, NewEmail, "changed"), (changed.Field, changed.OldValue, changed.NewValue, changed.How));
        harness.Sessions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_account_that_confirms_gets_a_fresh_session()
    {
        var harness = new Harness();
        harness.WithUser();
        harness.Accounts
            .Setup(p => p.ConfirmEmailChangeAsync(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailChange(42, Email, NewEmail));

        var result = await harness.Profile.ConfirmEmailChangeAsync("token", callerUserId: 42, "10.0.0.1");

        Assert.NotNull(result.Value);
        Assert.False(string.IsNullOrEmpty(result.Value.RefreshToken));
        harness.Sessions.Verify(p => p.InsertRefreshTokenAsync(It.Is<RefreshToken>(t => t.UserId == 42), "10.0.0.1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_spent_link_and_an_address_taken_since_are_refused()
    {
        var harness = new Harness();
        harness.Accounts
            .SetupSequence(p => p.ConfirmEmailChangeAsync(It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SecurityException("Email change token is not valid."))
            .ThrowsAsync(SqlExceptions.WithNumber(50021, "That address cannot be used."));

        var spent = await harness.Profile.ConfirmEmailChangeAsync("token", null, "10.0.0.1");
        var taken = await harness.Profile.ConfirmEmailChangeAsync("token", null, "10.0.0.1");

        Assert.Equal("EMAIL_CHANGE_TOKEN_INVALID", spent.Error.Code);
        Assert.Equal("EMAIL_CHANGE_CONFLICT", taken.Error.Code);
        Assert.Empty(harness.Mailer.Sent);
        harness.AccessTokens.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Reverting_puts_the_old_address_back_and_ends_every_session()
    {
        var harness = new Harness();
        harness.WithUser();
        harness.Accounts
            .Setup(p => p.RevertEmailChangeAsync(It.IsAny<byte[]>(), "10.0.0.1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailChange(42, NewEmail, Email));

        var result = await harness.Profile.RevertEmailChangeAsync("token", "10.0.0.1");

        Assert.True(result.IsSuccess);
        harness.AccessTokens.Verify(a => a.RevokeUserAsync(UserRef, It.IsAny<CancellationToken>()), Times.Once);
        var changed = Assert.Single(harness.Published.OfType<UserProfileChangedEvent>());
        Assert.Equal(("email", NewEmail, Email, "reverted"), (changed.Field, changed.OldValue, changed.NewValue, changed.How));
    }

    private sealed class Harness : AccountServices
    {
        public Harness() : this(new RecordingMailer(), new Mock<IEventBus>(), new Mock<IAuditWorkspaces>())
        {
        }

        private Harness(RecordingMailer mailer, Mock<IEventBus> bus, Mock<IAuditWorkspaces> audit)
            : base(bus: bus.Object, mailer: mailer, audit: audit.Object)
        {
            Mailer = mailer;
            audit.Setup(a => a.OfUserAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync([10]);
            bus.Setup(b => b.PublishAsync(It.IsAny<AccountEvent>(), It.IsAny<CancellationToken>()))
                .Callback<AccountEvent, CancellationToken>((e, _) => Published.Add(e))
                .Returns(Task.CompletedTask);
            Accounts.Setup(p => p.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SecurityException("No user with that email address."));
        }

        public RecordingMailer Mailer { get; }

        public List<AccountEvent> Published { get; } = [];

        public User WithUser(string username = "someone", DateTime? lockedUntil = null)
        {
            var user = new User
            {
                Id = 42,
                RefId = UserRef,
                Username = username,
                Email = Email,
                PasswordHash = new PasswordHasher<User>().HashPassword(new User(), Password),
                IsActive = true,
                IsEmailVerified = true,
                LockedUntil = lockedUntil
            };
            Accounts.Setup(p => p.GetByIdAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(user);
            return user;
        }
    }

    /// <summary>Records who was mailed what; the tokens are deliberately not captured.</summary>
    private sealed class RecordingMailer : IAuthMailer
    {
        public List<string> Sent { get; } = [];

        public bool IsConfigured => true;

        public ValueTask QueueInvitationAsync(string to, string tenantName, string token, DateTime expiresAtUtc, CancellationToken ct) => ValueTask.CompletedTask;

        public ValueTask QueueEmailVerificationAsync(string to, string username, string token, DateTime expiresAtUtc, CancellationToken ct) => ValueTask.CompletedTask;

        public ValueTask QueuePasswordResetAsync(string to, string username, string token, DateTime expiresAtUtc, CancellationToken ct) => ValueTask.CompletedTask;

        public ValueTask QueueAccountAlreadyExistsAsync(string to, string username, CancellationToken ct) => ValueTask.CompletedTask;

        public ValueTask QueueEmailChangeAsync(string to, string username, string token, DateTime expiresAtUtc, CancellationToken ct)
        {
            Sent.Add($"change:{to}");
            return ValueTask.CompletedTask;
        }

        public ValueTask QueueEmailChangedAsync(string to, string username, string newEmail, string revertToken, DateTime expiresAtUtc, CancellationToken ct)
        {
            Sent.Add($"changed:{to}->{newEmail}");
            return ValueTask.CompletedTask;
        }

        public ValueTask QueueBillingNoticeAsync(string to, string username, Wbskt.Auth.Host.Models.BillingNotice notice, string billingUrl, CancellationToken ct) => ValueTask.CompletedTask;
    }
}

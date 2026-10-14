using Microsoft.AspNetCore.Identity;
using Moq;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// The account aggregate: registration, confirming the address, and the password. Each test runs the
/// service against provider mocks and records what reached the bus.
/// </summary>
public sealed class AccountServiceTests
{
    private const string Password = "correct horse battery";
    private const string Email = "someone@example.test";
    private const string Ip = "10.0.0.1";
    private static readonly Guid UserRef = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ---------------------------------------------------------------- registration

    [Fact]
    public async Task Registering_stores_a_hash_and_never_the_password()
    {
        var harness = new Harness();
        User? inserted = null;
        harness.Accounts.Setup(p => p.InsertUserAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => inserted = u)
            .ReturnsAsync(42);
        harness.Accounts.Setup(p => p.GetByIdAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(AUser());

        var result = await harness.Registration.RegisterUserAsync("someone", Email, Password);

        Assert.True(result.IsSuccess);
        Assert.NotNull(inserted);
        Assert.NotEqual(Password, inserted.PasswordHash);
        Assert.NotEqual(PasswordVerificationResult.Failed, new PasswordHasher<User>().VerifyHashedPassword(inserted, inserted.PasswordHash, Password));
        // Everyone gets a tenant of their own, invited or not.
        harness.Tenants.Verify(p => p.CreateTenantAsync("someone's Tenant", null, 42, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        harness.Tenants.Verify(p => p.AcceptInvitationAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        var registered = Assert.IsType<UserRegisteredEvent>(Assert.Single(harness.Bus.Published));
        Assert.Equal((42, UserRef), (registered.UserId, registered.UserRefId));
    }

    [Fact]
    public async Task Registering_with_an_invitation_joins_the_inviting_tenant()
    {
        var harness = new Harness();
        var invitationRef = Guid.NewGuid();
        // Addressed in a different case: the address match ignores it, as the database does.
        harness.Tenants.Setup(p => p.GetInvitationByTokenHashAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvitationLookup(invitationRef, Guid.NewGuid(), "Acme", "Someone@Example.TEST", DateTime.UtcNow.AddDays(1), IsLive: true));
        harness.Accounts.Setup(p => p.InsertUserAsync(It.IsAny<User>(), It.IsAny<CancellationToken>())).ReturnsAsync(42);
        harness.Accounts.Setup(p => p.GetByIdAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(AUser());
        harness.Tenants.Setup(p => p.AcceptInvitationAsync(It.IsAny<byte[]>(), 42, It.IsAny<CancellationToken>())).ReturnsAsync(9);

        var result = await harness.Registration.RegisterUserAsync("someone", Email, Password, "invitation-token");

        Assert.True(result.IsSuccess);
        harness.Tenants.Verify(p => p.CreateTenantAsync("someone's Tenant", null, 42, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        harness.Tenants.Verify(p => p.AcceptInvitationAsync(It.Is<byte[]>(h => h.SequenceEqual(SecurityTokens.Hash("invitation-token"))), 42, It.IsAny<CancellationToken>()), Times.Once);
        var accepted = Assert.Single(harness.Bus.Published.OfType<InvitationAcceptedEvent>());
        Assert.Equal((invitationRef, 42, UserRef), (accepted.InvitationRefId, accepted.UserId, accepted.UserRefId));
    }

    [Fact]
    public async Task An_invitation_sent_to_another_address_creates_no_account()
    {
        var harness = new Harness();
        harness.Tenants.Setup(p => p.GetInvitationByTokenHashAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InvitationLookup(Guid.NewGuid(), Guid.NewGuid(), "Acme", "someone.else@example.test", DateTime.UtcNow.AddDays(1), IsLive: true));

        var result = await harness.Registration.RegisterUserAsync("someone", Email, Password, "invitation-token");

        Assert.Equal("INVITATION_INVALID", result.Error.Code);
        harness.Accounts.Verify(p => p.InsertUserAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(harness.Bus.Published);
    }

    [Fact]
    public async Task An_unknown_invitation_reads_like_a_misaddressed_one()
    {
        var harness = new Harness();
        harness.Tenants.Setup(p => p.GetInvitationByTokenHashAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SecurityException("No invitation."));

        var result = await harness.Registration.RegisterUserAsync("someone", Email, Password, "invitation-token");

        Assert.Equal("INVITATION_INVALID", result.Error.Code);
        harness.Accounts.Verify(p => p.InsertUserAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------- address verification

    [Fact]
    public async Task Confirming_the_address_is_announced_for_the_account()
    {
        var harness = new Harness();
        harness.Accounts.Setup(p => p.ConsumeEmailVerificationTokenAsync(It.Is<byte[]>(h => h.SequenceEqual(SecurityTokens.Hash("verify-token"))), It.IsAny<CancellationToken>())).ReturnsAsync(42);
        harness.Accounts.Setup(p => p.GetByIdAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(AUser());

        var result = await harness.Registration.VerifyEmailAsync("verify-token");

        Assert.True(result.IsSuccess);
        var verified = Assert.IsType<EmailVerifiedEvent>(Assert.Single(harness.Bus.Published));
        Assert.Equal((42, UserRef), (verified.UserId, verified.UserRefId));
    }

    [Fact]
    public async Task A_dead_confirmation_link_is_a_validation_error()
    {
        var harness = new Harness();
        harness.Accounts.Setup(p => p.ConsumeEmailVerificationTokenAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SecurityException("Email verification token is not valid."));

        var result = await harness.Registration.VerifyEmailAsync("verify-token");

        Assert.Equal("VERIFICATION_TOKEN_INVALID", result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(harness.Bus.Published);
    }

    // ---------------------------------------------------------------- passwords

    [Fact]
    public async Task A_reset_stores_a_new_hash_ends_access_tokens_and_is_announced()
    {
        var harness = new Harness();
        string? storedHash = null;
        harness.Accounts.Setup(p => p.ConsumePasswordResetTokenAsync(It.Is<byte[]>(h => h.SequenceEqual(SecurityTokens.Hash("reset-token"))), It.IsAny<string>(), Ip, It.IsAny<CancellationToken>()))
            .Callback<byte[], string, string?, CancellationToken>((_, hash, _, _) => storedHash = hash)
            .ReturnsAsync(42);
        harness.Accounts.Setup(p => p.GetByIdAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(AUser());

        var result = await harness.Passwords.ResetPasswordAsync("reset-token", "a brand new passphrase", Ip);

        Assert.True(result.IsSuccess);
        Assert.NotNull(storedHash);
        Assert.NotEqual("a brand new passphrase", storedHash);
        harness.AccessTokens.Verify(a => a.RevokeUserAsync(UserRef, It.IsAny<CancellationToken>()), Times.Once);
        var changed = Assert.IsType<PasswordChangedEvent>(Assert.Single(harness.Bus.Published));
        Assert.Equal(("reset", Ip), (changed.How, changed.ClientAddress));
    }

    [Fact]
    public async Task Changing_the_password_of_an_unknown_account_is_unauthorized()
    {
        var harness = new Harness();
        harness.Accounts.Setup(p => p.GetByIdAsync(42, It.IsAny<CancellationToken>())).ThrowsAsync(new SecurityException("User with id 42 not found."));

        var result = await harness.Passwords.ChangePasswordAsync(42, Password, "a brand new passphrase", Ip);

        Assert.Equal("AUTH_USER_NOT_FOUND", result.Error.Code);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
    }

    [Fact]
    public async Task Changing_the_password_hands_back_a_fresh_session()
    {
        var harness = new Harness();
        harness.Accounts.Setup(p => p.GetByIdAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(AUser());
        RefreshToken? inserted = null;
        harness.Sessions.Setup(p => p.InsertRefreshTokenAsync(It.IsAny<RefreshToken>(), Ip, It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, string, CancellationToken>((t, _, _) => inserted = t)
            .Returns(Task.CompletedTask);

        var result = await harness.Passwords.ChangePasswordAsync(42, Password, "a brand new passphrase", Ip);

        Assert.True(result.IsSuccess);
        harness.Accounts.Verify(p => p.ChangePasswordAsync(42, It.IsAny<string>(), Ip, It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(inserted);
        Assert.Equal(42, inserted.UserId);
        Assert.Equal(inserted.Token, result.Value.RefreshToken);
        var changed = Assert.IsType<PasswordChangedEvent>(Assert.Single(harness.Bus.Published));
        Assert.Equal("changed", changed.How);
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
        public Harness() : this(new RecordingBus())
        {
        }

        private Harness(RecordingBus bus) : base(bus: bus)
        {
            Bus = bus;
        }

        public RecordingBus Bus { get; }
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

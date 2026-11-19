using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Auth.Host.Services.Passwords;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// Sign-in, passwords and registration built over the same provider mocks, mailer and mail cooldown,
/// as the host wires them: a test that registers and then asks for a resend sees one cooldown, and
/// one that changes a password and then signs in sees one account.
/// </summary>
internal class AccountServices
{
    public AccountServices(
        IEventBus? bus = null,
        IAuthMailer? mailer = null,
        bool requireVerifiedEmail = true,
        IJwtService? jwt = null,
        IAuditWorkspaces? audit = null,
        ILogger<SignInService>? signInLogger = null,
        IBreachedPasswords? breachedPasswords = null,
        ITenantLimits? limits = null)
    {
        if (jwt is null)
        {
            var defaultJwt = new Mock<IJwtService>();
            defaultJwt.Setup(j => j.GenerateToken(It.IsAny<IEnumerable<Claim>>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).Returns("access-token");
            jwt = defaultJwt.Object;
        }

        bus ??= Mock.Of<IEventBus>();
        mailer ??= Mock.Of<IAuthMailer>();
        var cooldown = MailCooldownTests.InProcess();
        var metrics = new AuthMetrics();
        Notices = new SecurityNotices(mailer, cooldown, metrics, TimeProvider.System);

        var time = TimeProvider.System;
        var hashing = CredentialFakes.Hashing();
        var tokens = new SessionTokens(jwt, Options.Create(new AccessTokenOptions()), time);
        var announcer = new AccountAnnouncer(bus, audit ?? AuditWorkspaces.None, NullLogger<AccountAnnouncer>.Instance);
        var credentials = CredentialFakes.Credentials(Accounts, Sessions.Object, bus, Notices, AccessTokens.Object, hashing);

        SignIn = new SignInService(
            Accounts.Object,
            Sessions.Object,
            bus,
            Options.Create(new AuthEmailOptions { RequireVerifiedEmailForSignIn = requireVerifiedEmail }),
            signInLogger ?? NullLogger<SignInService>.Instance,
            metrics,
            AccessTokens.Object,
            tokens,
            credentials,
            hashing,
            announcer,
            time,
            audit);

        Passwords = new PasswordService(
            Accounts.Object,
            Sessions.Object,
            mailer,
            NullLogger<PasswordService>.Instance,
            metrics,
            tokens,
            cooldown,
            Notices,
            credentials,
            hashing,
            announcer,
            time,
            breachedPasswords);

        Profile = new AccountProfileService(
            Accounts.Object,
            Sessions.Object,
            mailer,
            cooldown,
            tokens,
            NullLogger<AccountProfileService>.Instance,
            metrics,
            credentials,
            announcer,
            time);

        Registration = new RegistrationService(
            Accounts.Object,
            Tenants.Object,
            bus,
            mailer,
            NullLogger<RegistrationService>.Instance,
            metrics,
            cooldown,
            limits ?? new FixedTenantLimits(),
            hashing,
            announcer,
            time,
            WorkspaceFakes.Created(),
            audit,
            breachedPasswords);
    }

    public Mock<IAccountProvider> Accounts { get; } = new();

    public Mock<ISessionProvider> Sessions { get; } = new();

    public Mock<ITenantProvider> Tenants { get; } = new();

    public Mock<IAccessTokenRevocation> AccessTokens { get; } = new();

    public SecurityNotices Notices { get; }

    public SignInService SignIn { get; }

    public PasswordService Passwords { get; }

    public RegistrationService Registration { get; }

    public AccountProfileService Profile { get; }
}

using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Services.Email;
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
        ILogger<SignInService>? signInLogger = null)
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

        SignIn = new SignInService(
            Accounts.Object,
            Sessions.Object,
            jwt,
            bus,
            Options.Create(new AuthEmailOptions { RequireVerifiedEmailForSignIn = requireVerifiedEmail }),
            signInLogger ?? NullLogger<SignInService>.Instance,
            metrics,
            AccessTokens.Object,
            Options.Create(new AccessTokenOptions()),
            audit);

        Passwords = new PasswordService(
            Accounts.Object,
            Sessions.Object,
            jwt,
            bus,
            mailer,
            NullLogger<PasswordService>.Instance,
            metrics,
            AccessTokens.Object,
            Options.Create(new AccessTokenOptions()),
            cooldown,
            audit);

        Registration = new RegistrationService(
            Accounts.Object,
            Tenants.Object,
            bus,
            mailer,
            NullLogger<RegistrationService>.Instance,
            metrics,
            cooldown,
            audit);
    }

    public Mock<IAccountProvider> Accounts { get; } = new();

    public Mock<ISessionProvider> Sessions { get; } = new();

    public Mock<ITenantProvider> Tenants { get; } = new();

    public Mock<IAccessTokenRevocation> AccessTokens { get; } = new();

    public SignInService SignIn { get; }

    public PasswordService Passwords { get; }

    public RegistrationService Registration { get; }
}

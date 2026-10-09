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
using Wbskt.Events.Auth;
using Wbskt.Infrastructure.Security;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Workflow.Engine.Host.Tests.AuthHost;

/// <summary>
/// Sign-ins and changes a person makes to their own account reach the audit log of every workspace
/// they belong to, with the address they came from.
/// </summary>
public sealed class AccountAuditEventTests
{
    private const string Password = "correct horse battery";
    private const string Email = "someone@example.test";

    private readonly Mock<IAuthProvider> _provider = new();
    private readonly Mock<IAuditWorkspaces> _audit = new();
    private readonly List<object> _published = [];
    private readonly AuthService _service;

    public AccountAuditEventTests()
    {
        var jwt = new Mock<IJwtService>();
        jwt.Setup(j => j.GenerateToken(It.IsAny<IEnumerable<Claim>>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).Returns("access-token");
        _audit.Setup(a => a.OfUserAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync([10, 11]);
        _audit.Setup(a => a.OfUserAsync(0, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        // Moq matches on the generic argument the caller binds, so each published type needs its own setup.
        var bus = new Mock<IEventBus>();
        bus.Setup(b => b.PublishAsync(It.IsAny<UserLoginFailedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<UserLoginFailedEvent, CancellationToken>((e, _) => _published.Add(e)).Returns(Task.CompletedTask);
        bus.Setup(b => b.PublishAsync(It.IsAny<UserLoginSuccessEvent>(), It.IsAny<CancellationToken>()))
            .Callback<UserLoginSuccessEvent, CancellationToken>((e, _) => _published.Add(e)).Returns(Task.CompletedTask);
        bus.Setup(b => b.PublishAsync(It.IsAny<AccountEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AccountEvent, CancellationToken>((e, _) => _published.Add(e)).Returns(Task.CompletedTask);

        _service = new AuthService(
            _provider.Object,
            jwt.Object,
            bus.Object,
            Mock.Of<IAuthMailer>(),
            Options.Create(new AuthEmailOptions()),
            NullLogger<AuthService>.Instance,
            new AuthMetrics(),
            Mock.Of<IAccessTokenRevocation>(),
            Options.Create(new AccessTokenOptions()),
            MailCooldownTests.InProcess(),
            _audit.Object);

        var user = new User
        {
            Id = 42,
            RefId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Username = "someone",
            Email = Email,
            PasswordHash = new PasswordHasher<User>().HashPassword(new User(), Password),
            IsActive = true,
            IsEmailVerified = true
        };
        _provider.Setup(p => p.GetByEmailAsync(Email, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _provider.Setup(p => p.GetByIdAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(user);
    }

    [Fact]
    public async Task A_wrong_password_is_logged_in_each_of_the_accounts_workspaces()
    {
        await _service.LoginAsync(Email, "not the password", "10.0.0.1");

        var failed = Assert.IsType<UserLoginFailedEvent>(Assert.Single(_published));
        Assert.Equal([10, 11], failed.WorkspaceIds);
        Assert.Equal("10.0.0.1", failed.IpAddress);
    }

    [Fact]
    public async Task A_sign_in_with_no_account_is_logged_nowhere()
    {
        _provider.Setup(p => p.GetByEmailAsync("nobody@example.test", It.IsAny<CancellationToken>())).ThrowsAsync(new SecurityException("no such user"));

        var result = await _service.LoginAsync("nobody@example.test", Password, "10.0.0.1");

        Assert.Equal("AUTH_INVALID_CREDENTIALS", result.Error.Code);

        var failed = Assert.IsType<UserLoginFailedEvent>(Assert.Single(_published));
        Assert.Empty(failed.WorkspaceIds);
    }

    [Fact]
    public async Task Changing_a_password_is_logged_with_where_it_came_from()
    {
        var result = await _service.ChangePasswordAsync(42, Password, "a brand new password", "10.0.0.1");

        Assert.True(result.IsSuccess);
        var changed = Assert.IsType<PasswordChangedEvent>(Assert.Single(_published));
        Assert.Equal("changed", changed.How);
        Assert.Equal("10.0.0.1", changed.ClientAddress);
        Assert.Equal([10, 11], changed.WorkspaceIds);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), changed.UserRefId);
    }

    [Fact]
    public async Task Signing_out_everywhere_is_logged()
    {
        await _service.LogoutAllAsync(42, "10.0.0.1");

        var revoked = Assert.IsType<SessionsRevokedEvent>(Assert.Single(_published));
        Assert.Equal("all", revoked.Which);
        Assert.Equal([10, 11], revoked.WorkspaceIds);
    }
}

using Wbskt.Primitives.Exceptions;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.Auth.Models;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Workflow.Engine.Host.Tests.AuthHost;

/// <summary>
/// Sign-in never writes the address it was given to the logs, and the alert a sign-in fault raises
/// carries neither the address nor the exception's text, because event logs are readable by tenant
/// admins. A fingerprint of the address stands in for it.
/// </summary>
public sealed class AuthLogRedactionTests
{
    private const string Password = "correct horse battery";
    private const string Email = "Someone.Private@example.test";

    [Fact]
    public async Task An_unknown_address_is_logged_as_a_fingerprint()
    {
        var harness = new Harness();
        harness.Provider.Setup(p => p.GetByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SecurityException($"No user with email {Email}."));

        await harness.Service.LoginAsync(Email, Password, "10.0.0.1");

        harness.AssertNoAddressLogged();
        Assert.Contains(harness.Logger.Messages, m => m.Contains(EmailFingerprint.Of(Email)));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Refused_sign_ins_log_the_user_id_not_the_address(bool rightPassword, bool verified)
    {
        var harness = new Harness();
        harness.WithUser(AUser(verified: verified));

        await harness.Service.LoginAsync(Email, rightPassword ? Password : "wrong", "10.0.0.1");

        harness.AssertNoAddressLogged();
    }

    [Fact]
    public async Task A_sign_in_fault_raises_an_alert_without_the_address_or_the_exception_text()
    {
        var harness = new Harness();
        harness.WithUser(AUser(verified: true));
        harness.Provider.Setup(p => p.RecordLoginSuccessAsync(42, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException($"Violation of UNIQUE KEY constraint. The duplicate key value is ({Email})."));

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.LoginAsync(Email, Password, "10.0.0.1"));

        var alert = Assert.Single(harness.Published.OfType<SecurityAlertEvent>());
        Assert.Equal("LoginFailure", alert.AlertType);
        foreach (var text in new[] { alert.Message, alert.Metadata ?? "" })
        {
            Assert.DoesNotContain("example.test", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UNIQUE KEY", text);
        }

        Assert.Contains(nameof(InvalidOperationException), alert.Message);
        Assert.Contains(EmailFingerprint.Of(Email), alert.Metadata);
    }

    [Fact]
    public void The_fingerprint_ignores_case_and_surrounding_space()
    {
        Assert.Equal(EmailFingerprint.Of("someone.private@example.test"), EmailFingerprint.Of("  " + Email + " "));
        Assert.Equal(12, EmailFingerprint.Of(Email).Length);
        Assert.NotEqual(EmailFingerprint.Of(Email), EmailFingerprint.Of("other@example.test"));
        Assert.Equal("none", EmailFingerprint.Of(null));
    }

    private static User AUser(bool verified) => new()
    {
        Id = 42,
        RefId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Username = "someone",
        Email = Email,
        PasswordHash = new PasswordHasher<User>().HashPassword(new User(), Password),
        IsActive = true,
        IsEmailVerified = verified
    };

    private sealed class Harness
    {
        public Harness()
        {
            var jwt = new Mock<IJwtService>();
            jwt.Setup(j => j.GenerateToken(It.IsAny<IEnumerable<Claim>>(), It.IsAny<string>(), It.IsAny<TimeSpan>())).Returns("access-token");

            var bus = new Mock<IEventBus>();
            bus.Setup(b => b.PublishAsync(It.IsAny<BaseEvent>(), It.IsAny<CancellationToken>()))
                .Callback<BaseEvent, CancellationToken>((e, _) => Published.Add(e))
                .Returns(Task.CompletedTask);

            Service = new AuthService(
                Provider.Object,
                jwt.Object,
                bus.Object,
                Mock.Of<IAuthMailer>(),
                Options.Create(new AuthEmailOptions()),
                Logger,
                new AuthMetrics(),
                Mock.Of<IAccessTokenRevocation>(),
                Options.Create(new AccessTokenOptions()),
                MailCooldownTests.InProcess());
        }

        public Mock<IAuthProvider> Provider { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public List<BaseEvent> Published { get; } = [];

        public AuthService Service { get; }

        public void WithUser(User user)
        {
            Provider.Setup(p => p.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>())).ReturnsAsync(user);
            Provider.Setup(p => p.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        }

        public void AssertNoAddressLogged()
        {
            Assert.NotEmpty(Logger.Messages);
            Assert.DoesNotContain(Logger.Messages, m => m.Contains("example.test", StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed class RecordingLogger : ILogger<AuthService>
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _messages.Enqueue(formatter(state, exception) + (exception is null ? "" : " " + exception.Message));
    }
}

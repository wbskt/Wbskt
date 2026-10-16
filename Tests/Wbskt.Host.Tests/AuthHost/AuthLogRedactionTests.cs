using Wbskt.Primitives.Exceptions;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
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
using Wbskt.Events.System;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Middlewares;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// Sign-in never writes the address it was given to the logs, and the alert a sign-in fault raises
/// carries neither the address nor the exception's text, because event logs are readable by tenant
/// admins. A fingerprint of the address stands in for it. The same holds for the fault itself once
/// GlobalExceptionMiddleware logs and announces it, and for adding a workspace member by address.
/// </summary>
public sealed class AuthLogRedactionTests
{
    private const string Password = "correct horse battery";
    private const string Email = "Someone.Private@example.test";

    [Fact]
    public async Task An_unknown_address_is_logged_as_a_fingerprint()
    {
        var harness = new Harness();
        harness.Accounts.Setup(p => p.GetByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SecurityException($"No user with email {Email}."));

        await harness.SignIn.LoginAsync(Email, Password, "10.0.0.1");

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

        await harness.SignIn.LoginAsync(Email, rightPassword ? Password : "wrong", "10.0.0.1");

        harness.AssertNoAddressLogged();
    }

    [Fact]
    public async Task A_sign_in_fault_raises_an_alert_without_the_address_or_the_exception_text()
    {
        var harness = new Harness();
        harness.WithUser(AUser(verified: true));
        harness.Accounts.Setup(p => p.RecordLoginSuccessAsync(42, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException($"Violation of UNIQUE KEY constraint. The duplicate key value is ({Email})."));

        await Assert.ThrowsAsync<SignInFaultException>(() => harness.SignIn.LoginAsync(Email, Password, "10.0.0.1"));

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
    public async Task A_sign_in_fault_reaches_the_middleware_without_the_address_or_the_exception_text()
    {
        var harness = new Harness();
        harness.WithUser(AUser(verified: true));
        harness.Accounts.Setup(p => p.RecordLoginSuccessAsync(42, It.IsAny<CancellationToken>()))
            .Returns(() => FailsDeepInTheProvider());
        var logger = new RecordingLogger<GlobalExceptionMiddleware>();
        var middleware = new GlobalExceptionMiddleware(
            async _ => await harness.SignIn.LoginAsync(Email, Password, "10.0.0.1"),
            logger);
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await middleware.InvokeAsync(context, harness.Bus);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        var body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
        Assert.Contains("INTERNAL_ERROR", body);
        Assert.Contains("An unexpected error occurred while processing the request.", body);

        var fault = Assert.Single(harness.Published.OfType<SystemErrorEvent>());
        foreach (var text in logger.Messages.Append(fault.Message).Append(fault.StackTrace ?? "").Append(body))
        {
            Assert.DoesNotContain("example.test", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UNIQUE KEY", text);
        }

        Assert.Contains(nameof(InvalidOperationException), fault.Message);
        // The original's frames survive, so the log still says where the fault happened.
        Assert.Contains(nameof(FailsDeepInTheProvider), fault.StackTrace);
        Assert.Contains(logger.Messages, m => m.Contains(nameof(FailsDeepInTheProvider)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Adding_an_unknown_member_keeps_the_address_out_of_the_logs(bool inAnotherTenant)
    {
        var auth = new Mock<IAccountProvider>();
        var workspaces = new Mock<IWorkspaceProvider>();
        if (inAnotherTenant)
        {
            auth.Setup(p => p.GetByEmailAsync(Email, It.IsAny<CancellationToken>())).ReturnsAsync(AUser(verified: true));
            workspaces.Setup(p => p.AddUserToWorkspaceAsync(7, 42, It.IsAny<CancellationToken>()))
                .ThrowsAsync(SqlExceptions.WithNumber(50009, $"User {Email} is not a member of the tenant."));
        }
        else
        {
            auth.Setup(p => p.GetByEmailAsync(Email, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SecurityException($"User with email {Email} not found."));
        }

        var serviceLogger = new RecordingLogger<WorkspaceService>();
        var service = new WorkspaceService(workspaces.Object, auth.Object, Mock.Of<ITenantProvider>(), Mock.Of<IAccessControlProvider>(), Mock.Of<IEventBus>(), serviceLogger, new AuthMetrics());

        var result = await service.AddUserToWorkspaceAsync(7, new AddMemberRequest(Email));

        Assert.Equal("USER_NOT_FOUND", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        // The controller answers through ApiErrorResults, which logs the error's message.
        var controllerLogger = new RecordingLogger<WorkspaceService>();
        ApiErrorResults.From(result.Error, controllerLogger);

        var logged = serviceLogger.Messages.Concat(controllerLogger.Messages).ToList();
        Assert.NotEmpty(controllerLogger.Messages);
        Assert.DoesNotContain(logged, m => m.Contains("example.test", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("example.test", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_fingerprint_ignores_case_and_surrounding_space()
    {
        Assert.Equal(EmailFingerprint.Of("someone.private@example.test"), EmailFingerprint.Of("  " + Email + " "));
        Assert.Equal(12, EmailFingerprint.Of(Email).Length);
        Assert.NotEqual(EmailFingerprint.Of(Email), EmailFingerprint.Of("other@example.test"));
        Assert.Equal("none", EmailFingerprint.Of(null));
    }

    [Fact]
    public void The_fingerprint_is_keyed_so_an_address_list_cannot_reverse_it()
    {
        // An unkeyed hash of the address is what a reader with a list of candidates would compute.
        var unkeyed = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(Email.ToLowerInvariant())))[..12];
        Assert.NotEqual(unkeyed, EmailFingerprint.Of(Email));

        var dedicated = EmailFingerprint.KeyFrom("a-dedicated-fingerprint-key", "signing-key");
        Assert.Equal(dedicated, EmailFingerprint.KeyFrom(" a-dedicated-fingerprint-key ", "another-signing-key"));
        var derived = EmailFingerprint.KeyFrom(null, "signing-key");
        Assert.Equal(derived, EmailFingerprint.KeyFrom("", "signing-key"));
        Assert.NotEqual(derived, EmailFingerprint.KeyFrom(null, "rotated-signing-key"));
        Assert.NotEqual(derived, Encoding.UTF8.GetBytes("signing-key"));
        Assert.NotEqual(EmailFingerprint.KeyFrom(null, null), EmailFingerprint.KeyFrom(null, null));
        Assert.Throws<InvalidOperationException>(() => EmailFingerprint.KeyFrom("short", "signing-key"));
    }

    // Its own frame, so the test can tell the original's stack trace was carried over.
    private static async Task FailsDeepInTheProvider()
    {
        await Task.Yield();
        throw new InvalidOperationException($"Violation of UNIQUE KEY constraint. The duplicate key value is ({Email}).");
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

    private sealed class Harness : AccountServices
    {
        public Harness() : this(new RecordingLogger<SignInService>(), new Mock<IEventBus>())
        {
        }

        private Harness(RecordingLogger<SignInService> logger, Mock<IEventBus> bus) : base(bus: bus.Object, signInLogger: logger)
        {
            Logger = logger;
            Bus = bus.Object;
            bus.Setup(b => b.PublishAsync(It.IsAny<BaseEvent>(), It.IsAny<CancellationToken>()))
                .Callback<BaseEvent, CancellationToken>((e, _) => Published.Add(e))
                .Returns(Task.CompletedTask);
        }

        public RecordingLogger<SignInService> Logger { get; }

        public IEventBus Bus { get; }

        public List<BaseEvent> Published { get; } = [];

        public void WithUser(User user)
        {
            Accounts.Setup(p => p.GetByEmailAsync(user.Email, It.IsAny<CancellationToken>())).ReturnsAsync(user);
            Accounts.Setup(p => p.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        }

        public void AssertNoAddressLogged()
        {
            Assert.NotEmpty(Logger.Messages);
            Assert.DoesNotContain(Logger.Messages, m => m.Contains("example.test", StringComparison.OrdinalIgnoreCase));
        }
    }

    // The exception is written out whole, inner exceptions and stack included, as a log sink would.
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _messages.Enqueue(formatter(state, exception) + (exception is null ? "" : " " + exception));
    }
}

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services.Passwords;
using Wbskt.Auth.Host.Telemetry;

namespace Wbskt.Host.Tests.AuthHost;

/// <summary>
/// Refusing passwords that already leaked (#127). Only a five-character hash prefix leaves the host,
/// padding never matches, and an outage at HIBP lets the password through rather than blocking anyone.
/// </summary>
public sealed class BreachedPasswordTests
{
    private const string Leaked = "password123";
    private const string Fresh = "a long passphrase nobody has used";

    // ---------------------------------------------------------------- the client

    [Fact]
    public async Task A_leaked_password_is_found_and_only_its_prefix_is_sent()
    {
        var handler = new StubHandler(_ => Lines(Suffix(Leaked) + ":42", "0000000000000000000000000000000000A:0"));

        Assert.True(await Client(handler).IsBreachedAsync(Leaked, CancellationToken.None));

        var request = Assert.Single(handler.Requests);
        Assert.Equal($"/range/{Prefix(Leaked)}", request.RequestUri!.AbsolutePath);
        Assert.Equal("true", Assert.Single(request.Headers.GetValues("Add-Padding")));
        Assert.DoesNotContain(Suffix(Leaked), request.RequestUri.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_password_not_in_the_range_is_clean()
    {
        var handler = new StubHandler(_ => Lines("00000000000000000000000000000000001:3"));

        Assert.False(await Client(handler).IsBreachedAsync(Fresh, CancellationToken.None));
    }

    [Fact]
    public async Task A_padding_entry_with_a_count_of_zero_is_not_a_breach()
    {
        var handler = new StubHandler(_ => Lines(Suffix(Fresh) + ":0"));

        Assert.False(await Client(handler).IsBreachedAsync(Fresh, CancellationToken.None));
    }

    [Fact]
    public async Task Below_the_minimum_count_is_let_through()
    {
        var handler = new StubHandler(_ => Lines(Suffix(Leaked) + ":2"));

        Assert.False(await Client(handler, new BreachedPasswordOptions { MinimumBreachCount = 3 }).IsBreachedAsync(Leaked, CancellationToken.None));
    }

    [Fact]
    public async Task A_slow_hibp_fails_open()
    {
        var handler = new StubHandler(async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Lines(Suffix(Leaked) + ":42");
        });

        Assert.False(await Client(handler, new BreachedPasswordOptions { TimeoutMilliseconds = 50 }).IsBreachedAsync(Leaked, CancellationToken.None));
    }

    [Fact]
    public async Task An_error_from_hibp_fails_open()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        Assert.False(await Client(handler).IsBreachedAsync(Leaked, CancellationToken.None));
    }

    [Fact]
    public async Task The_callers_own_cancellation_is_not_swallowed()
    {
        var handler = new StubHandler(async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return Lines();
        });
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(handler).IsBreachedAsync(Leaked, cancelled.Token));
    }

    [Fact]
    public async Task Turned_off_it_asks_nobody()
    {
        var handler = new StubHandler(_ => Lines(Suffix(Leaked) + ":42"));

        Assert.False(await Client(handler, new BreachedPasswordOptions { Enabled = false }).IsBreachedAsync(Leaked, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    // ---------------------------------------------------------------- where passwords are set

    [Fact]
    public async Task Sign_up_refuses_a_leaked_password_before_creating_anything()
    {
        var harness = new AccountServices(breachedPasswords: new Known(Leaked));

        var result = await harness.Registration.RegisterUserAsync("ada", "ada@example.test", Leaked);

        Assert.Equal("PASSWORD_BREACHED", result.Error.Code);
        harness.Accounts.Verify(p => p.InsertUserAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Changing_to_a_leaked_password_changes_nothing()
    {
        var harness = new AccountServices(breachedPasswords: new Known(Leaked));
        harness.Accounts.Setup(p => p.GetByIdAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(new User
        {
            Id = 42,
            RefId = Guid.NewGuid(),
            Email = "ada@example.test",
            PasswordHash = new PasswordHasher<User>().HashPassword(new User(), Fresh),
            IsActive = true
        });

        var result = await harness.Passwords.ChangePasswordAsync(42, Fresh, Leaked, "10.0.0.1");

        Assert.Equal("PASSWORD_BREACHED", result.Error.Code);
        harness.Accounts.Verify(p => p.ChangePasswordAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resetting_to_a_leaked_password_leaves_the_link_unspent()
    {
        var harness = new AccountServices(breachedPasswords: new Known(Leaked));

        var result = await harness.Passwords.ResetPasswordAsync("token", Leaked, "10.0.0.1");

        Assert.Equal("PASSWORD_BREACHED", result.Error.Code);
        harness.Accounts.Verify(p => p.ConsumePasswordResetTokenAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static PwnedPasswordsClient Client(StubHandler handler, BreachedPasswordOptions? options = null) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://hibp.test/") },
            Options.Create(options ?? new BreachedPasswordOptions()),
            new AuthMetrics(),
            NullLogger<PwnedPasswordsClient>.Instance);

    private static string Hash(string password) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));

    private static string Prefix(string password) => Hash(password)[..5];

    private static string Suffix(string password) => Hash(password)[5..];

    private static HttpResponseMessage Lines(params string[] lines) =>
        new(HttpStatusCode.OK) { Content = new StringContent(string.Join("\r\n", lines)) };

    private sealed class Known(string breached) : IBreachedPasswords
    {
        public Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken) => Task.FromResult(password == breached);
    }

    private sealed class StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public StubHandler(Func<CancellationToken, HttpResponseMessage> respond) : this(ct => Task.FromResult(respond(ct)))
        {
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return respond(cancellationToken);
        }
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Wbskt.Auth.Host.Services.Email;

namespace Wbskt.Workflow.Engine.Host.Tests.AuthHost;

/// <summary>
/// The per-address brake on mail an anonymous caller can aim at someone else's inbox. These run the
/// in-process store; the Redis store runs the same rules in one script and is exercised end to end.
/// </summary>
public sealed class MailCooldownTests
{
    private const string Address = "someone@example.test";

    /// <summary>The default rules, kept in this process. What the AuthService tests construct.</summary>
    internal static MailCooldown InProcess(MailCooldownOptions? options = null, TimeProvider? time = null, IConnectionMultiplexer? redis = null) =>
        new(Options.Create(options ?? new MailCooldownOptions()), redis, time ?? TimeProvider.System, NullLogger<MailCooldown>.Instance);

    [Fact]
    public async Task The_same_kind_waits_out_the_cooldown()
    {
        var clock = new ManualClock();
        var cooldown = InProcess(new MailCooldownOptions { CooldownSeconds = 120, HourlyLimit = 0 }, clock);

        Assert.True(await cooldown.TryAcquireAsync(Address, MailKind.PasswordReset));
        clock.Advance(TimeSpan.FromSeconds(119));
        Assert.False(await cooldown.TryAcquireAsync(Address, MailKind.PasswordReset));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(await cooldown.TryAcquireAsync(Address, MailKind.PasswordReset));
    }

    [Fact]
    public async Task Kinds_and_addresses_are_counted_apart()
    {
        var cooldown = InProcess(new MailCooldownOptions { CooldownSeconds = 120, HourlyLimit = 0 });

        Assert.True(await cooldown.TryAcquireAsync(Address, MailKind.PasswordReset));
        Assert.True(await cooldown.TryAcquireAsync(Address, MailKind.EmailVerification));
        Assert.True(await cooldown.TryAcquireAsync("someone-else@example.test", MailKind.PasswordReset));
    }

    [Fact]
    public async Task Addresses_compare_without_case_or_surrounding_space()
    {
        var cooldown = InProcess();

        Assert.True(await cooldown.TryAcquireAsync(Address, MailKind.PasswordReset));
        Assert.False(await cooldown.TryAcquireAsync(" SomeOne@Example.TEST ", MailKind.PasswordReset));
    }

    [Fact]
    public async Task The_hourly_limit_covers_every_kind_and_resets_after_the_hour()
    {
        var clock = new ManualClock();
        var cooldown = InProcess(new MailCooldownOptions { CooldownSeconds = 60, HourlyLimit = 3 }, clock);

        Assert.True(await cooldown.TryAcquireAsync(Address, MailKind.PasswordReset));
        Assert.True(await cooldown.TryAcquireAsync(Address, MailKind.EmailVerification));
        Assert.True(await cooldown.TryAcquireAsync(Address, MailKind.AccountAlreadyExists));
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.False(await cooldown.TryAcquireAsync(Address, MailKind.PasswordReset));

        clock.Advance(TimeSpan.FromMinutes(55));
        Assert.True(await cooldown.TryAcquireAsync(Address, MailKind.PasswordReset));
    }

    /// <summary>
    /// Otherwise a flood of requests would keep pushing the owner's own request further out.
    /// </summary>
    [Fact]
    public async Task A_refusal_spends_nothing()
    {
        var clock = new ManualClock();
        var cooldown = InProcess(new MailCooldownOptions { CooldownSeconds = 120, HourlyLimit = 2 }, clock);

        Assert.True(await cooldown.TryAcquireAsync(Address, MailKind.PasswordReset));
        for (var i = 0; i < 10; i++)
        {
            Assert.False(await cooldown.TryAcquireAsync(Address, MailKind.PasswordReset));
        }

        clock.Advance(TimeSpan.FromSeconds(120));
        Assert.True(await cooldown.TryAcquireAsync(Address, MailKind.PasswordReset));
    }

    [Fact]
    public async Task Zero_turns_both_rules_off()
    {
        var cooldown = InProcess(new MailCooldownOptions { CooldownSeconds = 0, HourlyLimit = 0 });

        for (var i = 0; i < 10; i++)
        {
            Assert.True(await cooldown.TryAcquireAsync(Address, MailKind.PasswordReset));
        }
    }

    /// <summary>
    /// Commands against a disconnected multiplexer wait out their timeout, and this sits on an
    /// anonymous request path, so a Redis that is down is not asked at all.
    /// </summary>
    [Fact]
    public async Task A_disconnected_Redis_is_not_asked_and_the_rules_still_hold()
    {
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        redis.SetupGet(r => r.IsConnected).Returns(false);
        var cooldown = InProcess(redis: redis.Object);

        Assert.True(await cooldown.TryAcquireAsync(Address, MailKind.PasswordReset));
        Assert.False(await cooldown.TryAcquireAsync(Address, MailKind.PasswordReset));
    }

    [Fact]
    public async Task A_Redis_error_falls_back_to_this_process()
    {
        var database = new Mock<IDatabase>();
        database.Setup(d => d.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(new RedisConnectionException(ConnectionFailureType.SocketFailure, "gone"));
        var redis = new Mock<IConnectionMultiplexer>();
        redis.SetupGet(r => r.IsConnected).Returns(true);
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
        var cooldown = InProcess(redis: redis.Object);

        Assert.True(await cooldown.TryAcquireAsync(Address, MailKind.PasswordReset));
        Assert.False(await cooldown.TryAcquireAsync(Address, MailKind.PasswordReset));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}

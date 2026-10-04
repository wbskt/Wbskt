using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Workflow.Engine.Host.Tests.Infrastructure;

/// <summary>
/// The per-device cutoffs the management host writes and the socket host reads. Redis is mocked, so
/// these pin down what is written and how a stored cutoff is read; the raise-only rotation script
/// runs in Redis and is exercised end to end.
/// </summary>
public sealed class ClientTokenCutoffsTests
{
    private static readonly Guid ClientRef = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime Moment = new(2026, 10, 4, 12, 0, 0, 900, DateTimeKind.Utc);
    private static readonly long MomentSeconds = new DateTimeOffset(Moment).ToUnixTimeSeconds();

    [Fact]
    public async Task Revoking_refuses_every_token_until_none_could_still_be_alive()
    {
        var (cutoffs, database) = Create();

        await cutoffs.RevokeAsync(ClientRef);

        database.Verify(d => d.StringSetAsync(
            (RedisKey)$"wbskt:client-cutoff:{ClientRef}", (RedisValue)long.MaxValue, ClientTokenCutoffs.Retention,
            It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
        Assert.True(ClientTokenCutoffs.Retention > ClientTokenCutoffs.TokenLifetime);
    }

    [Fact]
    public async Task Rotating_raises_the_cutoff_to_the_rotation_in_whole_seconds()
    {
        var (cutoffs, database) = Create();

        await cutoffs.RevokeIssuedBeforeAsync(ClientRef, Moment);

        database.Verify(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.Is<RedisKey[]>(k => k.Single() == $"wbskt:client-cutoff:{ClientRef}"),
            It.Is<RedisValue[]>(v => (long)v[0] == MomentSeconds && (long)v[1] == (long)ClientTokenCutoffs.Retention.TotalMilliseconds),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task Reinstating_replaces_the_cutoff_with_that_moment()
    {
        var (cutoffs, database) = Create();

        await cutoffs.ReinstateAsync(ClientRef, Moment);

        database.Verify(d => d.StringSetAsync(
            (RedisKey)$"wbskt:client-cutoff:{ClientRef}", (RedisValue)MomentSeconds, ClientTokenCutoffs.Retention,
            It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Theory]
    [InlineData(-60, true)]
    [InlineData(-1, true)]
    [InlineData(0, false)] // the cutoff's own second: the device that signed in straight after
    [InlineData(60, false)]
    public async Task A_token_is_refused_when_issued_before_the_stored_cutoff(int issuedOffsetSeconds, bool refused)
    {
        var (cutoffs, database) = Create();
        Stored(database, MomentSeconds);

        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(MomentSeconds + issuedOffsetSeconds).UtcDateTime;

        Assert.Equal(refused, await cutoffs.IsRevokedAsync(ClientRef, issuedAt));
    }

    [Fact]
    public async Task A_revoked_device_is_refused_whenever_its_token_was_issued()
    {
        var (cutoffs, database) = Create();
        Stored(database, long.MaxValue);

        Assert.True(await cutoffs.IsRevokedAsync(ClientRef, DateTime.UtcNow.AddMinutes(5)));
        Assert.True(await cutoffs.IsRevokedAsync(ClientRef, DateTime.MinValue));
    }

    [Fact]
    public async Task A_device_with_no_cutoff_is_accepted()
    {
        var (cutoffs, database) = Create();
        database.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(RedisValue.Null);

        Assert.False(await cutoffs.IsRevokedAsync(ClientRef, Moment));
    }

    /// <summary>
    /// Commands against a disconnected multiplexer wait out their timeout, and the read sits on every
    /// device connect, so a Redis that is down is not asked at all and the answer is "don't know".
    /// </summary>
    [Fact]
    public async Task A_disconnected_Redis_is_not_asked()
    {
        var redis = new Mock<IConnectionMultiplexer>(MockBehavior.Strict);
        redis.SetupGet(r => r.IsConnected).Returns(false);
        var cutoffs = new ClientTokenCutoffs(redis.Object, NullLogger<ClientTokenCutoffs>.Instance);

        Assert.Null(await cutoffs.IsRevokedAsync(ClientRef, Moment));
        await cutoffs.RevokeAsync(ClientRef);
    }

    [Fact]
    public async Task A_Redis_error_is_logged_not_thrown()
    {
        var (cutoffs, database) = Create();
        var gone = new RedisConnectionException(ConnectionFailureType.SocketFailure, "gone");
        database.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ThrowsAsync(gone);
        database.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ThrowsAsync(gone);

        Assert.Null(await cutoffs.IsRevokedAsync(ClientRef, Moment));
        await cutoffs.RevokeAsync(ClientRef);
    }

    [Fact]
    public async Task Without_Redis_configured_nothing_is_recorded_and_nothing_is_known()
    {
        var cutoffs = new ClientTokenCutoffs(null, NullLogger<ClientTokenCutoffs>.Instance);

        await cutoffs.RevokeAsync(ClientRef);

        Assert.Null(await cutoffs.IsRevokedAsync(ClientRef, Moment));
    }

    private static (ClientTokenCutoffs Cutoffs, Mock<IDatabase> Database) Create()
    {
        var database = new Mock<IDatabase>();
        var redis = new Mock<IConnectionMultiplexer>();
        redis.SetupGet(r => r.IsConnected).Returns(true);
        redis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
        return (new ClientTokenCutoffs(redis.Object, NullLogger<ClientTokenCutoffs>.Instance), database);
    }

    private static void Stored(Mock<IDatabase> database, long cutoff) =>
        database.Setup(d => d.StringGetAsync((RedisKey)$"wbskt:client-cutoff:{ClientRef}", It.IsAny<CommandFlags>()))
            .ReturnsAsync((RedisValue)cutoff);
}

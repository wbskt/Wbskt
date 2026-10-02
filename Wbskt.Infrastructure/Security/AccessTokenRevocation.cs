using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Wbskt.Infrastructure.Security;

public sealed class AccessTokenOptions
{
    /// <summary>
    /// <c>Jwt:AccessTokenLifetime</c>. Short, because an access token is checked against nothing but
    /// its signature and the revocation watermark; the refresh token carries the session.
    /// </summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);
}

/// <summary>Ends a user's access tokens before they expire.</summary>
public interface IAccessTokenRevocation
{
    /// <summary>Every access token issued to <paramref name="userId"/> up to now stops validating, on every host.</summary>
    Task RevokeUserAsync(int userId, CancellationToken cancellationToken = default);

    bool IsRevoked(int userId, DateTimeOffset issuedAt);
}

/// <summary>
/// A per-user watermark: a token issued at or before the moment its user was revoked is refused.
/// </summary>
/// <remarks>
/// <para>
/// The watermarks live in one Redis sorted set (member: user id, score: unix seconds) and in a local
/// copy on every validating host, so the check on the request path is a dictionary lookup and Redis
/// being slow or down never adds latency or fails a request. A revocation is published as well as
/// stored, so other hosts apply it within moments; a periodic resync from the set covers anything
/// published while a subscriber was disconnected, and a host that starts later picks up the lot.
/// </para>
/// <para>
/// An entry is only useful while a token issued before it could still be alive, so entries older
/// than the token lifetime plus clock skew are pruned, here and in Redis. Revocation is best effort:
/// if Redis is unreachable the refresh tokens are still revoked in the database, and the cost is
/// that already-issued access tokens live out their (short) lifetime.
/// </para>
/// <para>
/// <c>iat</c> has second resolution, so a token issued in the same second as the revocation cannot be
/// told apart from one issued just before it. Those are let through: every way of minting a token
/// after a revocation already needs a credential the revocation ended (the refresh tokens) or that
/// only the user holds (the new password), and refusing them would sign out someone who logs back in
/// immediately after resetting their password.
/// </para>
/// </remarks>
public sealed class AccessTokenRevocation : IAccessTokenRevocation, IHostedService, IDisposable
{
    internal const string SetKey = "wbskt:access-token-revocations";
    internal const string Channel = "wbskt:access-token-revocations";
    internal static readonly TimeSpan ResyncInterval = TimeSpan.FromSeconds(30);

    private readonly IConnectionMultiplexer? _redis;
    private readonly TimeProvider _time;
    private readonly ILogger<AccessTokenRevocation> _logger;
    private readonly TimeSpan _retention;
    private readonly ConcurrentDictionary<int, long> _watermarks = new();
    private CancellationTokenSource? _stopping;
    private Task? _resync;

    public AccessTokenRevocation(IConnectionMultiplexer? redis, IOptions<AccessTokenOptions> options, TimeProvider time, ILogger<AccessTokenRevocation> logger)
    {
        _redis = redis;
        _time = time;
        _logger = logger;
        _retention = options.Value.AccessTokenLifetime + JwtTrust.ClockSkew + TimeSpan.FromMinutes(1);
    }

    public async Task RevokeUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        long now = _time.GetUtcNow().ToUnixTimeSeconds();
        Apply(userId, now);

        if (_redis is null)
        {
            return;
        }

        try
        {
            IDatabase db = _redis.GetDatabase();
            await db.SortedSetAddAsync(SetKey, userId, now);
            await db.PublishAsync(RedisChannel.Literal(Channel), $"{userId}:{now}");
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogError(ex, "Could not publish the access-token revocation for user {UserId}; other hosts will accept that user's current access tokens until they expire", userId);
        }
    }

    public bool IsRevoked(int userId, DateTimeOffset issuedAt)
    {
        if (!_watermarks.TryGetValue(userId, out long watermark))
        {
            return false;
        }

        return issuedAt.ToUnixTimeSeconds() < watermark;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_redis is null)
        {
            _logger.LogWarning("ConnectionStrings:Redis is not configured; access-token revocation applies to this host only");
            return;
        }

        try
        {
            await _redis.GetSubscriber().SubscribeAsync(RedisChannel.Literal(Channel), (_, message) => OnMessage(message));
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            // The resync loop still brings revocations in, on its own interval.
            _logger.LogError(ex, "Could not subscribe to access-token revocations; relying on the periodic resync");
        }

        _stopping = new CancellationTokenSource();
        _resync = ResyncLoopAsync(_stopping.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stopping is null || _resync is null)
        {
            return;
        }

        await _stopping.CancelAsync();
        await _resync.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    public void Dispose() => _stopping?.Dispose();

    internal async Task ResyncAsync()
    {
        long cutoff = _time.GetUtcNow().Subtract(_retention).ToUnixTimeSeconds();

        foreach ((int userId, long watermark) in _watermarks)
        {
            if (watermark < cutoff)
            {
                _watermarks.TryRemove(new KeyValuePair<int, long>(userId, watermark));
            }
        }

        if (_redis is null)
        {
            return;
        }

        IDatabase db = _redis.GetDatabase();
        await db.SortedSetRemoveRangeByScoreAsync(SetKey, double.NegativeInfinity, cutoff, Exclude.Stop);
        foreach (SortedSetEntry entry in await db.SortedSetRangeByScoreWithScoresAsync(SetKey, cutoff, double.PositiveInfinity))
        {
            if (int.TryParse(entry.Element.ToString(), out int userId))
            {
                Apply(userId, (long)entry.Score);
            }
        }
    }

    private async Task ResyncLoopAsync(CancellationToken stopping)
    {
        using var timer = new PeriodicTimer(ResyncInterval, _time);
        try
        {
            while (true)
            {
                try
                {
                    await ResyncAsync();
                }
                catch (Exception ex) when (ex is RedisException or TimeoutException)
                {
                    _logger.LogWarning(ex, "Access-token revocation resync failed; retrying in {Interval}", ResyncInterval);
                }

                await timer.WaitForNextTickAsync(stopping);
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
    }

    private void OnMessage(RedisValue message)
    {
        string? text = message;
        int separator = text?.IndexOf(':') ?? -1;
        if (separator > 0
            && int.TryParse(text.AsSpan(0, separator), out int userId)
            && long.TryParse(text.AsSpan(separator + 1), out long watermark))
        {
            Apply(userId, watermark);
        }
    }

    private void Apply(int userId, long watermark) =>
        _watermarks.AddOrUpdate(userId, watermark, (_, existing) => Math.Max(existing, watermark));
}

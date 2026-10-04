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

    /// <summary>
    /// <c>Jwt:RefreshTokenLifetime</c>. How long a session survives without being used: every refresh
    /// issues a token good for this long again.
    /// </summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// <c>Jwt:SessionLifetime</c>. How long a session survives however often it is used, counted from
    /// the sign-in. Past it the refresh token is refused and the user enters their password again, so
    /// a refresh token that keeps being rotated, by its owner or by whoever stole it, still ends.
    /// </summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromDays(30);
}

/// <summary>Ends a user's access tokens before they expire.</summary>
public interface IAccessTokenRevocation
{
    /// <summary>Every access token issued to <paramref name="userId"/> up to now stops validating, on every host.</summary>
    Task RevokeUserAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every access token carrying <paramref name="sessionId"/> as its <c>sid</c> stops validating, on
    /// every host. Call it after the session's refresh token is revoked, so no new one can be minted.
    /// </summary>
    Task RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    bool IsRevoked(int userId, DateTimeOffset issuedAt);

    bool IsSessionRevoked(Guid sessionId);
}

/// <summary>
/// A per-user watermark: a token issued at or before the moment its user was revoked is refused. And
/// a set of ended sessions: a token whose <c>sid</c> is in it is refused whenever it was issued, since
/// a session's refresh token is revoked before the session is, so nothing can mint another.
/// </summary>
/// <remarks>
/// <para>
/// The watermarks live in one Redis sorted set and in a local copy on every validating host, so the
/// check on the request path is a dictionary lookup and Redis being slow or down never adds latency
/// or fails a request. A revocation is published as well as stored, so other hosts apply it within
/// moments; a periodic resync from the set covers anything published while a subscriber was
/// disconnected, and a host that starts later picks up the lot.
/// </para>
/// <para>
/// An entry is only useful while a token issued before it could still be alive. How long that is
/// depends on the lifetime of the tokens being revoked, which only the issuing host knows - a host
/// that merely validates them may be configured differently, or not at all. So the host recording a
/// revocation (always the issuer: the auth host) stamps it with an expiry of its own token lifetime
/// plus clock skew, and every host, here and in Redis, prunes by that stamp rather than by its own
/// configuration. In the set the member is <c>userId:watermark</c>, or <c>s:sessionId</c> for an
/// ended session, and the score is the expiry.
/// Revocation is best effort: if Redis is unreachable the refresh tokens are still revoked in the
/// database, and the cost is that already-issued access tokens live out their (short) lifetime.
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
    private const string SessionPrefix = "s:";

    private readonly ConcurrentDictionary<int, Watermark> _watermarks = new();

    // Ended session -> when the entry can go (unix seconds), by the same rule as a watermark.
    private readonly ConcurrentDictionary<Guid, long> _sessions = new();
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
        long expiresAt = now + (long)_retention.TotalSeconds;
        Apply(userId, new Watermark(now, expiresAt));

        if (_redis is null)
        {
            return;
        }

        try
        {
            IDatabase db = _redis.GetDatabase();
            await db.SortedSetAddAsync(SetKey, $"{userId}:{now}", expiresAt);
            await db.PublishAsync(RedisChannel.Literal(Channel), $"{userId}:{now}:{expiresAt}");
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogError(ex, "Could not publish the access-token revocation for user {UserId}; other hosts will accept that user's current access tokens until they expire", userId);
        }
    }

    public async Task RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        long expiresAt = _time.GetUtcNow().ToUnixTimeSeconds() + (long)_retention.TotalSeconds;
        ApplySession(sessionId, expiresAt);

        if (_redis is null)
        {
            return;
        }

        try
        {
            IDatabase db = _redis.GetDatabase();
            await db.SortedSetAddAsync(SetKey, $"{SessionPrefix}{sessionId:N}", expiresAt);
            await db.PublishAsync(RedisChannel.Literal(Channel), $"{SessionPrefix}{sessionId:N}:{expiresAt}");
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogError(ex, "Could not publish the end of session {SessionId}; other hosts will accept its current access token until it expires", sessionId);
        }
    }

    public bool IsSessionRevoked(Guid sessionId) => _sessions.ContainsKey(sessionId);

    public bool IsRevoked(int userId, DateTimeOffset issuedAt)
    {
        if (!_watermarks.TryGetValue(userId, out Watermark? watermark))
        {
            return false;
        }

        return issuedAt.ToUnixTimeSeconds() < watermark.RevokedAt;
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
        long now = _time.GetUtcNow().ToUnixTimeSeconds();

        foreach ((int userId, Watermark watermark) in _watermarks)
        {
            if (watermark.ExpiresAt < now)
            {
                _watermarks.TryRemove(new KeyValuePair<int, Watermark>(userId, watermark));
            }
        }

        foreach ((Guid sessionId, long expiresAt) in _sessions)
        {
            if (expiresAt < now)
            {
                _sessions.TryRemove(new KeyValuePair<Guid, long>(sessionId, expiresAt));
            }
        }

        if (_redis is null)
        {
            return;
        }

        IDatabase db = _redis.GetDatabase();
        await db.SortedSetRemoveRangeByScoreAsync(SetKey, double.NegativeInfinity, now, Exclude.Stop);
        foreach (SortedSetEntry entry in await db.SortedSetRangeByScoreWithScoresAsync(SetKey, now, double.PositiveInfinity))
        {
            string member = entry.Element.ToString();
            if (member.StartsWith(SessionPrefix, StringComparison.Ordinal))
            {
                if (Guid.TryParse(member.AsSpan(SessionPrefix.Length), out Guid sessionId))
                {
                    ApplySession(sessionId, (long)entry.Score);
                }
            }
            else if (TryParse(member, out int userId, out long revokedAt))
            {
                Apply(userId, new Watermark(revokedAt, (long)entry.Score));
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

    internal void OnMessage(RedisValue message)
    {
        string? text = message;
        int last = text?.LastIndexOf(':') ?? -1;
        if (last > 0 && text!.StartsWith(SessionPrefix, StringComparison.Ordinal))
        {
            if (Guid.TryParse(text.AsSpan(SessionPrefix.Length, last - SessionPrefix.Length), out Guid sessionId)
                && long.TryParse(text.AsSpan(last + 1), out long sessionExpiresAt))
            {
                ApplySession(sessionId, sessionExpiresAt);
            }
        }
        else if (last > 0
            && TryParse(text.AsSpan(0, last), out int userId, out long revokedAt)
            && long.TryParse(text.AsSpan(last + 1), out long expiresAt))
        {
            Apply(userId, new Watermark(revokedAt, expiresAt));
        }
    }

    /// <summary>Parses <c>userId:revokedAt</c>.</summary>
    private static bool TryParse(ReadOnlySpan<char> text, out int userId, out long revokedAt)
    {
        int separator = text.IndexOf(':');
        revokedAt = 0;
        userId = 0;
        return separator > 0
            && int.TryParse(text[..separator], out userId)
            && long.TryParse(text[(separator + 1)..], out revokedAt);
    }

    private void Apply(int userId, Watermark watermark) =>
        _watermarks.AddOrUpdate(userId, watermark, (_, existing) => new Watermark(
            Math.Max(existing.RevokedAt, watermark.RevokedAt),
            Math.Max(existing.ExpiresAt, watermark.ExpiresAt)));

    private void ApplySession(Guid sessionId, long expiresAt) =>
        _sessions.AddOrUpdate(sessionId, expiresAt, (_, existing) => Math.Max(existing, expiresAt));

    /// <summary>Tokens issued before <see cref="RevokedAt"/> are refused until <see cref="ExpiresAt"/>, both unix seconds.</summary>
    private sealed record Watermark(long RevokedAt, long ExpiresAt);
}

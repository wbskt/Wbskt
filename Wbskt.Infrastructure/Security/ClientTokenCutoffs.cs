using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Wbskt.Infrastructure.Security;

/// <summary>Ends a device's tokens before they expire, on every socket host. See <see cref="ClientTokenCutoffs"/>.</summary>
public interface IClientTokenCutoffs
{
    /// <summary>Refuses every token the device holds (it was revoked, rejected or deleted).</summary>
    Task RevokeAsync(Guid clientRefId);

    /// <summary>Refuses tokens issued before <paramref name="rotatedAt"/> (its secret was rotated).</summary>
    Task RevokeIssuedBeforeAsync(Guid clientRefId, DateTime rotatedAt);

    /// <summary>
    /// The device may connect again, with a token issued from <paramref name="reinstatedAt"/> on. It
    /// could not sign in while it had no access, so any older token predates the revocation.
    /// </summary>
    Task ReinstateAsync(Guid clientRefId, DateTime reinstatedAt);

    /// <summary>
    /// Whether a token for the device issued at <paramref name="issuedAt"/> (UTC) is refused, or
    /// <c>null</c> when Redis could not say.
    /// </summary>
    Task<bool?> IsRevokedAsync(Guid clientRefId, DateTime issuedAt, CancellationToken cancellationToken = default);
}

/// <summary>
/// Per-device cutoffs in Redis: a device token issued before its device's cutoff is refused. The
/// management host writes one whenever a device loses access, before it answers; the socket host
/// reads it when a device connects.
/// </summary>
/// <remarks>
/// <para>
/// A device token lives for <see cref="TokenLifetime"/> and is checked against nothing but its
/// signature, so revoking a device, deleting it or rotating its secret has to reach the socket host
/// some other way. The events that do that are kept in memory there, so a socket host that restarts,
/// or misses one, would let a revoked device back in until its token ran out. The cutoff in Redis is
/// what makes revocation hold regardless; the events stay as the fast path that also closes the live
/// connection.
/// </para>
/// <para>
/// One key per device, <c>wbskt:client-cutoff:&lt;clientRef&gt;</c>, holding unix seconds, and kept
/// only as long as a token it refuses could still be alive. Revoking or deleting a device sets it to
/// the end of time; rotating the secret raises it to the rotation, never lowers it; approving the
/// device again sets it to that moment, so the device signs in afresh, as the SDK does on every
/// connect anyway, and a token from before it lost access stays refused.
/// </para>
/// <para>
/// Best effort, like access-token revocation: with Redis unreachable the write is logged and the
/// event still goes out, and the read answers "don't know" so the socket host falls back to its own
/// list. A device can connect while Redis is down only if the socket host also missed its event.
/// </para>
/// </remarks>
public sealed class ClientTokenCutoffs : IClientTokenCutoffs
{
    /// <summary>How long a device token is valid for. The management host issues them for this long.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    internal const string KeyPrefix = "wbskt:client-cutoff:";

    /// <summary>A cutoff no token can be issued before: every token is refused.</summary>
    internal const long Forever = long.MaxValue;

    // A token issued before the cutoff is alive for at most its lifetime plus clock skew.
    internal static readonly TimeSpan Retention = TokenLifetime + JwtTrust.ClockSkew + TimeSpan.FromMinutes(1);

    // Rotation only ever raises the cutoff: a rotation landing after a revocation must not undo it.
    private const string RaiseScript = """
        local current = tonumber(redis.call('GET', KEYS[1]))
        if current == nil or current < tonumber(ARGV[1]) then
          redis.call('SET', KEYS[1], ARGV[1], 'PX', ARGV[2])
        else
          redis.call('PEXPIRE', KEYS[1], ARGV[2])
        end
        return 1
        """;

    // The connect path waits no longer than this for an answer before falling back.
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(1);

    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<ClientTokenCutoffs> _logger;

    public ClientTokenCutoffs(IConnectionMultiplexer? redis, ILogger<ClientTokenCutoffs> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public Task RevokeAsync(Guid clientRefId) =>
        WriteAsync(clientRefId, "revoke", db => db.StringSetAsync(Key(clientRefId), Forever, Retention));

    public Task RevokeIssuedBeforeAsync(Guid clientRefId, DateTime rotatedAt) =>
        WriteAsync(clientRefId, "rotation", db => db.ScriptEvaluateAsync(
            RaiseScript, [Key(clientRefId)], [ToUnixSeconds(rotatedAt), (long)Retention.TotalMilliseconds]));

    public Task ReinstateAsync(Guid clientRefId, DateTime reinstatedAt) =>
        WriteAsync(clientRefId, "reinstatement", db => db.StringSetAsync(Key(clientRefId), ToUnixSeconds(reinstatedAt), Retention));

    public async Task<bool?> IsRevokedAsync(Guid clientRefId, DateTime issuedAt, CancellationToken cancellationToken = default)
    {
        // A disconnected multiplexer would wait out its command timeout, on every connect.
        if (_redis is not { IsConnected: true })
        {
            return null;
        }

        try
        {
            var value = await _redis.GetDatabase().StringGetAsync(Key(clientRefId)).WaitAsync(ReadTimeout, cancellationToken);
            if (value.IsNull || !long.TryParse(value.ToString(), out var cutoff))
            {
                return false;
            }

            // iat has whole seconds, and so does the cutoff, so a token from the cutoff's own second
            // is accepted: the device that signs in straight after a rotation or a re-approval.
            return ToUnixSeconds(issuedAt) < cutoff;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogWarning(ex, "Could not read the token cutoff for client {ClientRefId}; relying on this host's revocation list", clientRefId);
            return null;
        }
    }

    internal static string Key(Guid clientRefId) => $"{KeyPrefix}{clientRefId}";

    private static long ToUnixSeconds(DateTime utc) =>
        utc == DateTime.MinValue ? long.MinValue : new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    private async Task WriteAsync(Guid clientRefId, string what, Func<IDatabase, Task> write)
    {
        if (_redis is null)
        {
            return;
        }

        if (!_redis.IsConnected)
        {
            _logger.LogError("Redis is unavailable; the {What} of client {ClientRefId} reaches the socket hosts by event only", what, clientRefId);
            return;
        }

        try
        {
            await write(_redis.GetDatabase());
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogError(ex, "Could not record the {What} of client {ClientRefId} in Redis; it reaches the socket hosts by event only", what, clientRefId);
        }
    }
}

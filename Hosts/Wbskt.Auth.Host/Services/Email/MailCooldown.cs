using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Wbskt.Auth.Host.Services.Email;

/// <summary>The mail an anonymous caller can make this host send to an address of their choosing.</summary>
internal enum MailKind
{
    EmailVerification,
    PasswordReset,
    AccountAlreadyExists
}

/// <summary>Bound from <c>Auth:MailCooldown</c>.</summary>
public sealed class MailCooldownOptions
{
    /// <summary>How long after one mail of a kind the same address gets no other of that kind. 0 turns it off.</summary>
    public int CooldownSeconds { get; init; } = 120;

    /// <summary>The most mail of any of these kinds one address gets in an hour. 0 turns it off.</summary>
    public int HourlyLimit { get; init; } = 5;
}

/// <summary>
/// Decides whether a mail may go to an address now. Forgot-password, resend-verification and
/// registering with a taken address all send mail to whatever address the caller names, and the
/// per-IP rate limit does nothing against a caller with a handful of addresses. Without this, anyone
/// could flood someone's inbox from our domain and take our sender reputation down with it.
/// </summary>
/// <remarks>
/// <para>
/// Callers answer exactly as they would have had the mail gone out, so a suppressed mail is
/// invisible to the caller. Ask before issuing a token, not after: issuing one supersedes the
/// previous link, and superseding it without a new mail would leave the owner holding a dead link.
/// </para>
/// <para>
/// Kept in Redis when it is configured and connected, so it holds across replicas and restarts,
/// and in this process otherwise. Addresses are stored as a hash, never in the clear. A refusal
/// spends nothing, so a flood of requests does not push the owner's own request further out.
/// </para>
/// </remarks>
internal sealed class MailCooldown
{
    private const string KeyPrefix = "wbskt:mail-cooldown";
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    // Both checks and both writes in one round trip, so two concurrent requests cannot both pass.
    private const string AcquireScript = """
        local cooldown = tonumber(ARGV[1])
        local limit = tonumber(ARGV[2])
        if cooldown > 0 and redis.call('EXISTS', KEYS[1]) == 1 then return 0 end
        if limit > 0 then
          if tonumber(redis.call('GET', KEYS[2]) or '0') >= limit then return 0 end
          if redis.call('INCR', KEYS[2]) == 1 then redis.call('EXPIRE', KEYS[2], ARGV[3]) end
        end
        if cooldown > 0 then redis.call('SET', KEYS[1], '1', 'EX', cooldown) end
        return 1
        """;

    // Entries live an hour at most and the per-IP limit bounds how fast they arrive; this only stops
    // a long-running process from holding expired ones forever.
    private const int PruneAbove = 10_000;

    private readonly IConnectionMultiplexer? _redis;
    private readonly TimeProvider _time;
    private readonly ILogger<MailCooldown> _logger;
    private readonly TimeSpan _cooldown;
    private readonly int _hourlyLimit;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _cooldowns = new();
    private readonly Dictionary<string, (int Count, DateTimeOffset Until)> _hourly = new();

    public MailCooldown(IOptions<MailCooldownOptions> options, IConnectionMultiplexer? redis, TimeProvider time, ILogger<MailCooldown> logger)
    {
        _cooldown = TimeSpan.FromSeconds(Math.Max(0, options.Value.CooldownSeconds));
        _hourlyLimit = Math.Max(0, options.Value.HourlyLimit);
        _redis = redis;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// True, and counted, when a mail of <paramref name="kind"/> may go to <paramref name="address"/>
    /// now. False when it should be dropped. Never throws.
    /// </summary>
    public async Task<bool> TryAcquireAsync(string address, MailKind kind)
    {
        if (_cooldown == TimeSpan.Zero && _hourlyLimit == 0)
        {
            return true;
        }

        var id = AddressId(address);

        // Checked first so a Redis that is down costs nothing: commands against a disconnected
        // multiplexer wait out their timeout, and this sits on an anonymous request path.
        if (_redis is { IsConnected: true })
        {
            try
            {
                var allowed = await _redis.GetDatabase().ScriptEvaluateAsync(
                    AcquireScript,
                    [$"{KeyPrefix}:{kind}:{id}", $"{KeyPrefix}:hour:{id}"],
                    [(int)_cooldown.TotalSeconds, _hourlyLimit, (int)Window.TotalSeconds]);
                return (int)allowed == 1;
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                _logger.LogWarning(ex, "Mail cooldown could not reach Redis; counting in this process instead");
            }
        }

        return TryAcquireInProcess(id, kind);
    }

    private bool TryAcquireInProcess(string id, MailKind kind)
    {
        var now = _time.GetUtcNow();
        var cooldownKey = $"{kind}:{id}";

        lock (_gate)
        {
            if (_cooldowns.Count + _hourly.Count > PruneAbove)
            {
                Prune(now);
            }

            if (_cooldown > TimeSpan.Zero && _cooldowns.TryGetValue(cooldownKey, out var until) && until > now)
            {
                return false;
            }

            if (_hourlyLimit > 0)
            {
                var hour = _hourly.TryGetValue(id, out var h) && h.Until > now ? h : (Count: 0, Until: now + Window);
                if (hour.Count >= _hourlyLimit)
                {
                    return false;
                }

                _hourly[id] = (hour.Count + 1, hour.Until);
            }

            if (_cooldown > TimeSpan.Zero)
            {
                _cooldowns[cooldownKey] = now + _cooldown;
            }

            return true;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var key in _cooldowns.Where(e => e.Value <= now).Select(e => e.Key).ToList())
        {
            _cooldowns.Remove(key);
        }

        foreach (var key in _hourly.Where(e => e.Value.Until <= now).Select(e => e.Key).ToList())
        {
            _hourly.Remove(key);
        }
    }

    // Case-insensitive, matching how the database compares addresses.
    private static string AddressId(string address) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address.Trim().ToLowerInvariant())), 0, 16);
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Services;

/// <summary>Bound from <c>Auth:SignUpChallenge</c>.</summary>
public sealed class SignUpChallengeOptions
{
    /// <summary>Whether register refuses a request without a solved challenge.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Leading zero bits the solution's hash needs. Each extra bit doubles the average work: 18 is
    /// about 260,000 hashes, a second or two in a browser, and nothing for a person signing up once.
    /// </summary>
    public int Difficulty { get; init; } = 18;

    /// <summary>How long an issued challenge can be redeemed.</summary>
    public int LifetimeSeconds { get; init; } = 600;

    /// <summary>
    /// Base64 HMAC key that signs challenges. Unset, each process makes its own, which is right for
    /// one replica: a restart only voids the challenges issued in the last few minutes. More than one
    /// auth replica needs a shared key here.
    /// </summary>
    public string? Key { get; init; }
}

/// <summary>
/// The proof of work that registration asks for. Every registration creates a tenant and a
/// workspace, so a script that signs up in a loop is cheap to run and costly to store; this makes
/// each attempt cost the caller CPU time, without a third-party CAPTCHA that tracks people.
/// </summary>
/// <remarks>
/// <para>
/// A challenge is <c>v1.{difficulty}.{expiresUnix}.{nonce}.{hmac}</c>. It is stateless until it is
/// redeemed: the HMAC proves this host issued it, and nothing is stored when it is issued, so asking
/// for challenges costs this host one hash each. Redeeming it records it as spent until it expires,
/// in Redis when connected and in this process otherwise, so one solution cannot register twice.
/// </para>
/// <para>
/// The difficulty is part of the signed challenge, so changing the setting does not void the
/// challenges already handed out.
/// </para>
/// </remarks>
public sealed class SignUpChallenges
{
    private const string Version = "v1";
    private const string KeyPrefix = "wbskt:signup-challenge";
    private const int MaxDifficulty = 32;

    // Entries live as long as a challenge at most, and the per-IP limit bounds how fast they arrive;
    // this only stops a long-running process from holding expired ones forever.
    private const int PruneAbove = 10_000;

    public static readonly Error Required = Error.Validation(
        "SIGNUP_CHALLENGE_REQUIRED",
        "Registration needs a solved challenge. Fetch one from GET /api/auth/register/challenge.");

    // One answer for forged, expired, unsolved and spent challenges: the client's response to each
    // is the same, fetch a new one and solve it.
    public static readonly Error Invalid = Error.Validation(
        "SIGNUP_CHALLENGE_INVALID",
        "The sign-up challenge is not valid. It may have expired or already been used. Fetch a new one.");

    private readonly SignUpChallengeOptions _options;
    private readonly byte[] _key;
    private readonly int _difficulty;
    private readonly TimeSpan _lifetime;
    private readonly IConnectionMultiplexer? _redis;
    private readonly TimeProvider _time;
    private readonly AuthMetrics _metrics;
    private readonly ILogger<SignUpChallenges> _logger;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _spent = new();

    public SignUpChallenges(
        IOptions<SignUpChallengeOptions> options,
        IConnectionMultiplexer? redis,
        TimeProvider time,
        AuthMetrics metrics,
        ILogger<SignUpChallenges> logger)
    {
        _options = options.Value;
        _key = string.IsNullOrEmpty(_options.Key)
            ? RandomNumberGenerator.GetBytes(32)
            : Convert.FromBase64String(_options.Key);
        _difficulty = Math.Clamp(_options.Difficulty, 0, MaxDifficulty);
        _lifetime = TimeSpan.FromSeconds(Math.Max(1, _options.LifetimeSeconds));
        _redis = redis;
        _time = time;
        _metrics = metrics;
        _logger = logger;
    }

    public SignUpChallengeResponse Issue()
    {
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds((_time.GetUtcNow() + _lifetime).ToUnixTimeSeconds());
        var nonce = SecurityTokens.Generate();
        var payload = string.Join('.', Version, _difficulty.ToString(CultureInfo.InvariantCulture), expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), nonce);
        return new SignUpChallengeResponse($"{payload}.{Sign(payload)}", _difficulty, expiresAt);
    }

    /// <summary>
    /// Succeeds, and spends the challenge, when <paramref name="solution"/> solves a live challenge
    /// this host issued. Always succeeds while the check is turned off.
    /// </summary>
    public async Task<Result> RedeemAsync(string? challenge, string? solution)
    {
        if (!_options.Enabled)
        {
            return Result.Success();
        }

        if (string.IsNullOrEmpty(challenge) || string.IsNullOrEmpty(solution))
        {
            _metrics.RecordRegistration("challenge_missing");
            return Result.Failure(Required);
        }

        if (!TryRead(challenge, out var difficulty, out var expiresAt)
            || expiresAt <= _time.GetUtcNow()
            || !SignUpProofOfWork.Satisfies(challenge, solution, difficulty)
            || !await TrySpendAsync(challenge, expiresAt))
        {
            _metrics.RecordRegistration("challenge_invalid");
            return Result.Failure(Invalid);
        }

        return Result.Success();
    }

    private bool TryRead(string challenge, out int difficulty, out DateTimeOffset expiresAt)
    {
        difficulty = 0;
        expiresAt = default;

        var parts = challenge.Split('.');
        if (parts.Length != 5 || parts[0] != Version)
        {
            return false;
        }

        var payload = challenge[..challenge.LastIndexOf('.')];
        var expected = Encoding.ASCII.GetBytes(Sign(payload));
        var presented = Encoding.ASCII.GetBytes(parts[4]);
        if (!CryptographicOperations.FixedTimeEquals(expected, presented))
        {
            return false;
        }

        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out difficulty)
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var expiresUnix))
        {
            return false;
        }

        expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresUnix);
        return true;
    }

    private string Sign(string payload)
    {
        var mac = HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(payload));
        return Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private async Task<bool> TrySpendAsync(string challenge, DateTimeOffset expiresAt)
    {
        var id = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(challenge)), 0, 16);
        var remaining = expiresAt - _time.GetUtcNow();
        if (remaining < TimeSpan.FromSeconds(1))
        {
            remaining = TimeSpan.FromSeconds(1);
        }

        // Checked first so a Redis that is down costs nothing: commands against a disconnected
        // multiplexer wait out their timeout, and this sits on an anonymous request path.
        if (_redis is { IsConnected: true })
        {
            try
            {
                return await _redis.GetDatabase().StringSetAsync($"{KeyPrefix}:{id}", "1", remaining, When.NotExists);
            }
            catch (Exception ex) when (ex is RedisException or TimeoutException)
            {
                _logger.LogWarning(ex, "Sign-up challenges could not reach Redis; recording spent challenges in this process instead");
            }
        }

        return TrySpendInProcess(id, expiresAt);
    }

    private bool TrySpendInProcess(string id, DateTimeOffset expiresAt)
    {
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            if (_spent.Count > PruneAbove)
            {
                foreach (var key in _spent.Where(e => e.Value <= now).Select(e => e.Key).ToList())
                {
                    _spent.Remove(key);
                }
            }

            return _spent.TryAdd(id, expiresAt);
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Services.Passwords;

/// <summary>Bound from <c>Auth:BreachedPasswordCheck</c>.</summary>
public sealed class BreachedPasswordOptions
{
    /// <summary>Off for self-hosted installs with no way out to the internet.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>How many breaches a password must appear in to be refused.</summary>
    public int MinimumBreachCount { get; init; } = 1;

    /// <summary>Past this the check gives up and lets the password through.</summary>
    public int TimeoutMilliseconds { get; init; } = 2000;

    public string BaseUrl { get; init; } = "https://api.pwnedpasswords.com/";
}

/// <summary>Whether a password being set has already leaked, so it is among the first a credential-stuffing run tries.</summary>
internal interface IBreachedPasswords
{
    /// <summary>True only when the password is known to be breached. Never throws for an outage: unknown is false.</summary>
    Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken);
}

internal static class BreachedPasswords
{
    public static readonly Error Breached =
        Error.Validation("PASSWORD_BREACHED", "This password has appeared in a data breach. Choose a different one.");

    /// <summary>For callers built without a checker, such as tests of something else.</summary>
    public static readonly IBreachedPasswords None = new NoCheck();

    private sealed class NoCheck : IBreachedPasswords
    {
        public Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}

/// <summary>
/// Have I Been Pwned's Pwned Passwords range API, by k-anonymity: only the first five hex characters
/// of the password's SHA-1 leave this host, and the match happens here. Padded responses, so the
/// size of the answer says nothing about the prefix either.
/// </summary>
/// <remarks>
/// Fails open. A slow or unreachable HIBP lets the password through with a warning, because an outage
/// there must not stop anyone signing up or recovering an account.
/// </remarks>
internal sealed class PwnedPasswordsClient(
    HttpClient http,
    IOptions<BreachedPasswordOptions> options,
    AuthMetrics metrics,
    ILogger<PwnedPasswordsClient> logger) : IBreachedPasswords
{
    public const string UserAgent = "Wbskt-Auth";

    private readonly BreachedPasswordOptions _options = options.Value;

    public async Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return false;
        }

        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        var prefix = hash[..5];
        var suffix = hash[5..];

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1, _options.TimeoutMilliseconds)));

        string body;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"range/{prefix}");
            request.Headers.Add("Add-Padding", "true");
            using var response = await http.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();
            body = await response.Content.ReadAsStringAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(ex, "The breached-password check could not reach HIBP; the password was accepted unchecked.");
            metrics.RecordBreachCheck("unavailable");
            return false;
        }

        var breached = Count(body, suffix) >= Math.Max(1, _options.MinimumBreachCount);
        metrics.RecordBreachCheck(breached ? "breached" : "clean");
        return breached;
    }

    // Lines are "SUFFIX:COUNT". Padding lines carry a count of 0, so they never match a threshold of 1 or more.
    private static int Count(string body, string suffix)
    {
        foreach (var line in body.AsSpan().EnumerateLines())
        {
            var colon = line.IndexOf(':');
            if (colon <= 0 || !line[..colon].Trim().Equals(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return int.TryParse(line[(colon + 1)..].Trim(), out var count) ? count : 0;
        }

        return 0;
    }
}

using System.Security.Cryptography;
using System.Text;

namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>
/// The Standard Webhooks signature (standardwebhooks.com), which Dodo Payments uses: an HMAC-SHA256 over
/// <c>{webhook-id}.{webhook-timestamp}.{raw body}</c> with the base64 secret after its <c>whsec_</c>
/// prefix, sent base64 as one or more space-separated <c>v1,{signature}</c> entries. A timestamp more
/// than <see cref="Tolerance"/> from now is refused, so a captured delivery cannot be replayed later.
/// </summary>
public static class StandardWebhookSignature
{
    public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public static bool IsValid(string secret, string id, string timestamp, string body, string signatureHeader, DateTimeOffset now)
    {
        if (!long.TryParse(timestamp, out long seconds) || Math.Abs((now - DateTimeOffset.FromUnixTimeSeconds(seconds)).TotalSeconds) > Tolerance.TotalSeconds)
        {
            return false;
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(secret.StartsWith("whsec_", StringComparison.Ordinal) ? secret["whsec_".Length..] : secret);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}"));
        foreach (string entry in signatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int comma = entry.IndexOf(',');
            if (comma < 0 || entry[..comma] != "v1")
            {
                continue;
            }

            byte[] given;
            try
            {
                given = Convert.FromBase64String(entry[(comma + 1)..]);
            }
            catch (FormatException)
            {
                continue;
            }

            if (CryptographicOperations.FixedTimeEquals(expected, given))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The header value a sender would compute; for tests and local replays.</summary>
    public static string Sign(string secret, string id, string timestamp, string body)
    {
        byte[] key = Convert.FromBase64String(secret.StartsWith("whsec_", StringComparison.Ordinal) ? secret["whsec_".Length..] : secret);
        return "v1," + Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}")));
    }
}

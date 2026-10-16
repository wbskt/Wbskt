using System.Security.Cryptography;
using System.Text;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Auth.Host.Telemetry;

/// <summary>
/// What logs and events say instead of an email address. Logs leave the database's access controls
/// (shipped to Loki, read by whoever operates it) and events land in the event log tenant admins can
/// read, so neither carries the address itself. The fingerprint still lets an operator see that a
/// run of failed sign-ins aimed at one account, and check a suspected address against it by hand.
/// </summary>
/// <remarks>
/// The fingerprint is keyed with a server-side secret, so someone who can read the logs and holds a
/// list of candidate addresses can't reverse it offline. Checking a suspect address needs the key:
/// <c>Auth:Telemetry:EmailFingerprintKey</c> when it is set, otherwise a key derived from the
/// host's <c>Jwt:SigningKey</c>. With the derived key, rotating the signing key changes every
/// fingerprint from then on. With neither (tests, a bare local run) each process uses a random key.
/// </remarks>
internal static class EmailFingerprint
{
    public const string KeyConfig = "Auth:Telemetry:EmailFingerprintKey";

    private static readonly byte[] DerivationInfo = "wbskt-auth-email-fingerprint"u8.ToArray();

    private static byte[] _key = RandomNumberGenerator.GetBytes(32);

    /// <summary>The first 12 hex characters of the HMAC-SHA256 of the trimmed, lower-cased address.</summary>
    public static string Of(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "none";
        }

        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()));
        return Convert.ToHexStringLower(mac)[..12];
    }

    /// <summary>Keys every later fingerprint from configuration; called once at startup.</summary>
    public static void UseKeyFrom(IConfiguration configuration) => UseKey(KeyFrom(configuration[KeyConfig], configuration[JwtSigningKeys.SigningKeyConfig]));

    internal static void UseKey(byte[] key) => _key = key;

    internal static byte[] KeyFrom(string? dedicatedKey, string? signingKey)
    {
        if (!string.IsNullOrWhiteSpace(dedicatedKey))
        {
            var bytes = Encoding.UTF8.GetBytes(dedicatedKey.Trim());
            if (bytes.Length < 16)
            {
                throw new InvalidOperationException($"{KeyConfig} must be at least 16 characters long.");
            }

            return bytes;
        }

        if (!string.IsNullOrWhiteSpace(signingKey))
        {
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(signingKey.Trim()), 32, info: DerivationInfo);
        }

        return RandomNumberGenerator.GetBytes(32);
    }
}

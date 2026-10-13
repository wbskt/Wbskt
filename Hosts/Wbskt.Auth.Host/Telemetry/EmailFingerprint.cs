using System.Security.Cryptography;
using System.Text;

namespace Wbskt.Auth.Host.Telemetry;

/// <summary>
/// What logs and events say instead of an email address. Logs leave the database's access controls
/// (shipped to Loki, read by whoever operates it) and events land in the event log tenant admins can
/// read, so neither carries the address itself. The fingerprint still lets an operator see that a
/// run of failed sign-ins aimed at one account, and check a suspected address against it by hand.
/// </summary>
internal static class EmailFingerprint
{
    /// <summary>The first 12 hex characters of the SHA-256 of the trimmed, lower-cased address.</summary>
    public static string Of(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "none";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()));
        return Convert.ToHexStringLower(hash)[..12];
    }
}

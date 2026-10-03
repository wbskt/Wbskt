using System.Security.Cryptography;
using System.Text;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Generates and hashes the opaque, single-use tokens this host mails out: tenant invitations,
/// password resets and email verifications. One generator for all three, deliberately — a second
/// copy of <see cref="Hash"/> that drifted from this one would make every token issued before the
/// drift silently unredeemable, and the properties each of these needs are identical.
/// <para>
/// <see cref="Hash"/> is also what refresh tokens are stored as. Those are minted by
/// <c>AuthService</c> (64 random bytes, base64) but hashed here for the same reason.
/// </para>
/// </summary>
internal static class SecurityTokens
{
    /// <summary>
    /// 256 bits from a cryptographic source, URL-safe so it survives being pasted into a link. Each
    /// of these is a bearer credential — for tenant membership, for an account, or for the right to
    /// set a password — so they are generated the way a refresh token is rather than from a
    /// <see cref="Guid"/>, which is neither unpredictable nor meant to be.
    /// </summary>
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Hashes the token as the client sends it back — the string, not the bytes behind it — so what
    /// is compared at redemption is exactly what was issued.
    /// <para>
    /// Plain SHA-256, deliberately: this is a high-entropy random value rather than a password, so
    /// there is no dictionary for a work factor to slow down, and adding one would only cost latency
    /// on every redemption. The digest is 32 bytes, matching the <c>TokenHash VARBINARY(32)</c>
    /// column on each of the tables that store one.
    /// </para>
    /// </summary>
    public static byte[] Hash(string token)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}

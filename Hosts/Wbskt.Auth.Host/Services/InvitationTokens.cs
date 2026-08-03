using System.Security.Cryptography;
using System.Text;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Generates and hashes invitation tokens. Shared because two services sit on either side of the
/// same value — <see cref="IManagementService"/> issues it, and registration redeems it for an
/// invitee who has no account yet. A second copy of <see cref="Hash"/> that drifted from this one
/// would make every previously issued invitation silently unredeemable.
/// </summary>
internal static class InvitationTokens
{
    /// <summary>
    /// 256 bits from a cryptographic source, URL-safe so it survives being pasted into an invitation
    /// link. This is a bearer credential for tenant membership, so it is generated the way a refresh
    /// token is rather than from a <see cref="Guid"/>, which is neither unpredictable nor meant to be.
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
    /// on every redemption. The digest is 32 bytes, matching <c>TenantInvitations.TokenHash</c>.
    /// </para>
    /// </summary>
    public static byte[] Hash(string token)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}

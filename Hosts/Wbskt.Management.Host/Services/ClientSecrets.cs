using System.Security.Cryptography;
using System.Text;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// Generation, hashing and comparison of the secret a device authenticates with. One place, for the
/// same reason <c>InvitationTokens</c> in the auth host is one place: a second copy
/// of the hash that drifted from this one would reject every device on the platform at once, and by
/// then the plaintext needed to diagnose it is gone.
/// </summary>
internal static class ClientSecrets
{
    /// <summary>Bytes of entropy in a generated secret.</summary>
    private const int SecretBytes = 32;

    /// <summary>
    /// A new secret, returned to the registering device once and never stored in this form.
    /// </summary>
    public static string Generate()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes));
    }

    /// <summary>
    /// Hashes the secret as the device sends it back — the string, not the bytes behind it.
    /// </summary>
    /// <remarks>
    /// SHA-256 rather than a password hasher, deliberately. <see cref="Generate"/> produces 32 bytes
    /// straight from the CSPRNG, so there is no dictionary to search and nothing for a deliberately
    /// slow hash to slow down; meanwhile client tokens last an hour, so an entire fleet
    /// re-authenticates hourly and the cost would be paid over and over. User passwords are a
    /// different problem and correctly use <c>PasswordHasher</c>.
    ///
    /// The UTF-8 encoding here has to match the <c>CONVERT(VARCHAR(255), ...)</c> in the migration
    /// backfill: <c>HASHBYTES</c> over an <c>NVARCHAR</c> would hash UTF-16LE and produce a
    /// different digest for the same secret. Base64 is pure ASCII, which is what makes the two
    /// agree. <c>ClientSecretHashingIntegrationTests</c> pins them together.
    /// </remarks>
    public static byte[] Hash(string secret)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    /// <summary>
    /// Whether <paramref name="presented"/> hashes to <paramref name="storedHash"/>, in time that
    /// does not depend on how much of the digest matched.
    /// </summary>
    public static bool Matches(byte[]? storedHash, string? presented)
    {
        if (storedHash is null || string.IsNullOrEmpty(presented))
        {
            return false;
        }

        // FixedTimeEquals is length-sensitive but not content-sensitive, so a stored hash of the
        // wrong length is rejected here rather than throwing.
        return storedHash.Length == ClientCredential.SecretHashBytes
               && CryptographicOperations.FixedTimeEquals(storedHash, Hash(presented));
    }
}

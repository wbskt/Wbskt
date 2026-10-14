using System.Security.Cryptography;
using System.Text;

namespace Wbskt.Infrastructure.Security;

/// <summary>
/// The one hash for high-entropy secrets that are stored only as a digest: the auth host's mailed
/// tokens and refresh tokens, and the management host's device secrets. A second copy that drifted
/// from this one would make every stored digest unmatchable at once.
/// </summary>
public static class SecretHash
{
    /// <summary>
    /// SHA-256 over the UTF-8 bytes of <paramref name="secret"/> as presented (the string, not the
    /// bytes behind it): 32 bytes, matching the <c>VARBINARY(32)</c> columns that store them.
    /// </summary>
    public static byte[] Sha256(string secret)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }
}

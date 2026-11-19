using System.Security.Cryptography;
using System.Text;

namespace Wbskt.Workflow.Runtime.BranchResults;

/// <summary>
/// RefIds for branches a replayed step must not create twice: the same name always gives the same id,
/// so <c>CreateIfAbsentAsync</c> finds the branch the first attempt made instead of adding another.
/// </summary>
internal static class DerivedRefIds
{
    public static Guid Of(string name)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(name));
        byte[] bytes = digest[..16];

        // Stamp it as a name-based (version 5 layout, RFC 4122 variant) GUID so it reads as one.
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}

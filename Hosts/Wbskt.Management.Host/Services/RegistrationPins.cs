using System.Security.Cryptography;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// Generation of the enrollment PIN a device presents to join a workspace.
/// </summary>
/// <remarks>
/// The PIN is the only credential on an anonymous endpoint: whoever knows it gets a device into the
/// workspace, auto-approved if the policy says so. It used to be six hex characters cut from a
/// NEWID() inside RegistrationPolicy_Create - sixteen million values shared by every policy on the
/// platform, so guessing at random finds *some* tenant's policy quickly. Twelve characters from a
/// thirty-symbol alphabet is about 59 bits, drawn from the CSPRNG.
///
/// The alphabet drops 0/O, 1/I/L and U: a PIN is read off a screen and typed into a device, so
/// characters that look alike are a support call. Lookups are by the database's case-insensitive
/// collation, so a PIN typed in lower case still matches.
/// </remarks>
internal static class RegistrationPins
{
    public const int Length = 12;

    private const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Generate()
    {
        return RandomNumberGenerator.GetString(Alphabet, Length);
    }
}

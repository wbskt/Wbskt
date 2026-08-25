namespace Wbskt.Management.Host.Models;

/// <summary>
/// A client together with the hash of the secret it authenticates with.
///
/// Separate from <see cref="Client"/> so the hash travels only on the one path that needs it. The
/// client model itself carries no secret in any form, which means no ordinary read of a client can
/// put credential material into a response, a log line or an event payload.
/// </summary>
/// <param name="Client">The client record, as every other lookup returns it.</param>
/// <param name="SecretHash">SHA-256 of the client's secret. Compare with <c>ClientSecrets.Matches</c>.</param>
public sealed record ClientCredential(Client Client, byte[] SecretHash)
{
    /// <summary>
    /// Length of a SHA-256 digest, matching <c>Clients.SecretHash VARBINARY(32)</c>. Lives on the
    /// model so both the provider (which sizes the SQL parameter) and <c>ClientSecrets</c> (which
    /// validates before comparing) can use it without either depending on the other.
    /// </summary>
    public const int SecretHashBytes = 32;
}

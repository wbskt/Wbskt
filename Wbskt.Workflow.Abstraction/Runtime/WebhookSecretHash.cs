using System.Security.Cryptography;
using System.Text;

namespace Wbskt.Workflow.Abstraction.Runtime;

/// <summary>
/// The form a webhook caller's presented secret travels and is compared in. The management host
/// hashes what the caller sent before queueing the call, so the secret itself is never written to the
/// broker (where a call that keeps failing ends up on an error queue); the engine hashes the
/// trigger's configured secret the same way and compares the two.
/// </summary>
public static class WebhookSecretHash
{
    /// <summary>Lower-case hex SHA-256 of the UTF-8 secret; null for no secret.</summary>
    public static string? Of(string? secret) =>
        string.IsNullOrEmpty(secret) ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}

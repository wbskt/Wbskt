using System.Security.Claims;

namespace Wbskt.Infrastructure.Security;

/// <summary>The claims of a device (client) token: issued by the management host, accepted by the socket host.</summary>
public static class ClientClaims
{
    /// <summary>Says what kind of principal the token is for; <see cref="ClientType"/> for a device.</summary>
    public const string Type = "type";
    public const string ClientType = "client";

    /// <summary>The client's internal id.</summary>
    public const string Id = "id";
    public const string WorkspaceId = "workspace_id";
    public const string PolicyRef = "policy_ref";
    public const string IssuedAt = "iat";

    /// <summary>The authorization policy the socket host's <c>/ws</c> requires: a device token and nothing else.</summary>
    public const string ClientOnlyPolicy = "ClientOnly";
}

/// <summary>Who a device token says the device is, read once from its claims.</summary>
public sealed record ClientIdentity(Guid ClientRefId, int ClientId, int WorkspaceId, DateTime IssuedAtUtc)
{
    /// <summary>
    /// Reads a device token's claims. False for anything that is not a well-formed device token, so a
    /// token missing a claim is refused rather than failing later. A token without a readable issue
    /// time reads as issued at the start of time, older than any revocation cutoff.
    /// </summary>
    public static bool TryRead(ClaimsPrincipal principal, out ClientIdentity identity)
    {
        identity = null!;
        if (principal.FindFirst(ClientClaims.Type)?.Value != ClientClaims.ClientType
            || !Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var clientRefId)
            || !int.TryParse(principal.FindFirst(ClientClaims.Id)?.Value, out var clientId)
            || !int.TryParse(principal.FindFirst(ClientClaims.WorkspaceId)?.Value, out var workspaceId))
        {
            return false;
        }

        var issuedAt = long.TryParse(principal.FindFirst(ClientClaims.IssuedAt)?.Value, out var iat)
            ? DateTimeOffset.FromUnixTimeSeconds(iat).UtcDateTime
            : DateTime.MinValue;

        identity = new ClientIdentity(clientRefId, clientId, workspaceId, issuedAt);
        return true;
    }
}

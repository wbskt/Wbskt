namespace Wbskt.Infrastructure.Security;

/// <summary>
/// Who signs which tokens, and who they are for. Two issuers, each with its own key pair, so that no
/// host can mint a token another host would accept on a different surface's behalf.
/// </summary>
/// <remarks>
/// <para>
/// Before this split every host signed and validated with one shared HS256 secret and checked neither
/// issuer nor audience. A device token and a console token were then interchangeable at the signature
/// layer, and the only thing that stopped a client token reaching the user API was that a client's
/// subject is a Guid while the identity middleware parses an int (AUTH_TK_06/07). With asymmetric keys
/// only the auth host can sign a user token and only the management host can sign a client token; the
/// socket host signs nothing. A compromised management host can impersonate devices, not people.
/// </para>
/// <para>
/// The values are identifiers, not URLs: they are compared, never dereferenced. Where to fetch a
/// trusted issuer's public keys is configuration (<c>Jwt:TrustedJwksUrl</c>), separate from its name.
/// </para>
/// </remarks>
public static class JwtIssuers
{
    /// <summary>Signs user access tokens. Private key: the auth host's <c>Jwt:SigningKey</c>.</summary>
    public const string Auth = "wbskt-auth";

    /// <summary>Signs client (device) tokens. Private key: the management host's <c>Jwt:SigningKey</c>.</summary>
    public const string Management = "wbskt-management";
}

public static class JwtAudiences
{
    /// <summary>The console-facing APIs: the auth host and the management host. User tokens only.</summary>
    public const string Api = "wbskt-api";

    /// <summary>The device data plane: the socket host's <c>/ws</c>. Client tokens only.</summary>
    public const string Socket = "wbskt-socket";
}

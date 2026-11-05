namespace Wbskt.Protocol;

// The client's two HTTP calls before it opens the socket: register under a policy with its PIN
// (POST api/v1/client-registrations/initiate), then log in with the secret it got
// (POST api/v1/client-auth/login) for the access token the socket takes.

public enum ClientStatus : byte
{
    Pending = 0,
    Registered = 1,
    Revoked = 2,
    Rejected = 3
}

public record ClientRegistrationRequest(string Pin, string Name);

public record ClientRegistrationResponse(
    Guid ClientRefId,
    string Secret,
    ClientStatus Status
);

public record ClientLoginRequest(Guid ClientRefId, string Secret);

public record ClientLoginResponse(string AccessToken, int ExpiresIn);

/// <summary>The <c>code</c> of a refused client login.</summary>
public static class ClientErrorCodes
{
    /// <summary>Unknown client or wrong secret. The credentials will never work again.</summary>
    public const string Unauthorized = "CLIENT_UNAUTHORIZED";

    /// <summary>Approval is still pending. Keep the credentials and retry.</summary>
    public const string NotRegistered = "CLIENT_NOT_REGISTERED";
}

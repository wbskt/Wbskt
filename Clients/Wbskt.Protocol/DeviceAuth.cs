namespace Wbskt.Protocol;

// The device's two HTTP calls before it opens the socket: register under a policy with its PIN
// (POST api/client-registrations/initiate), then log in with the secret it got
// (POST api/client-auth/login) for the access token the socket takes.

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

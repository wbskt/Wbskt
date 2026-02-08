namespace Webskt.Common.Abstraction.Models.Management;

public enum ClientStatus : byte
{
    Pending = 0,
    Registered = 1,
    Revoked = 2
}

public record RegistrationPolicyRequest(
    string Name, 
    int? MaxClients, 
    bool AutoApproval = true
);

public record RegistrationPolicyResponse(
    Guid RefId, 
    string Pin, 
    string Name, 
    int? MaxClients, 
    bool AutoApproval,
    DateTime CreatedAt
);

public record ClientRegistrationRequest(string Pin, string Name);

public record ClientRegistrationResponse(
    Guid ClientRefId, 
    string Secret, 
    ClientStatus Status
);

public record ClientResponse(
    Guid ClientRefId,
    Guid PolicyRefId,
    string Name,
    ClientStatus Status,
    DateTime CreatedAt
);

public record ClientLoginRequest(Guid ClientRefId, string Secret);

public record ClientLoginResponse(string AccessToken, int ExpiresIn);

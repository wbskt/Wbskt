using Wbskt.Models;

namespace Wbskt.Management.Host.Models;

public enum ClientStatus : byte
{
    Pending = 0,
    Registered = 1,
    Revoked = 2,
    Rejected = 3
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
    bool IsEnabled,
    DateTime CreatedAt,
    int RegisteredClientCount,
    int ConnectedClientCount
);

public record UpdateRegistrationPolicyRequest(
    string Name,
    int? MaxClients,
    bool AutoApproval,
    bool IsEnabled
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
    bool IsConnected,
    DateTime? ConnectedAt,
    DateTime? LastActivityAt,
    int? LastRttMs,
    DateTime CreatedAt
);

public record ClientDetailResponse(
    Guid ClientRefId,
    Guid PolicyRefId,
    string PolicyName,
    string Name,
    ClientStatus Status,
    bool IsConnected,
    DateTime? ConnectedAt,
    DateTime? LastActivityAt,
    int? LastRttMs,
    DateTime? RttMeasuredAt,
    string? AgentName,
    string? AgentVersion,
    string? Platform,
    IReadOnlyList<CommandCapability>? Capabilities,
    DateTime CreatedAt
);

public record ClientStateVariableResponse(
    string Name,
    string DataType,
    string Value,
    DateTime UpdatedAt
);

public record MessageTemplateRequest(
    string Name,
    string MessageType,
    string PayloadJson,
    Guid? PolicyRefId
);

public record MessageTemplateResponse(
    Guid RefId,
    string Name,
    string MessageType,
    string PayloadJson,
    Guid? PolicyRefId,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record ClientLoginRequest(Guid ClientRefId, string Secret);

public record ClientLoginResponse(string AccessToken, int ExpiresIn);

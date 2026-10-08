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

/// <summary>A client's new secret, shown once. The old one stops working at once.</summary>
public record ClientSecretResponse(Guid ClientRefId, string Secret);

/// <summary>One status change for many clients. At most <see cref="MaxClients"/> per request.</summary>
public record BulkClientStatusRequest(IReadOnlyList<Guid> ClientRefIds, ClientStatus Status)
{
    public const int MaxClients = 100;
}

/// <summary>
/// Which clients changed and which did not. A failure carries the same error code the single-client
/// endpoint would have returned, so one bad client does not undo the rest.
/// </summary>
public record BulkClientStatusResponse(IReadOnlyList<Guid> Updated, IReadOnlyList<BulkClientStatusFailure> Failed);

public record BulkClientStatusFailure(Guid ClientRefId, string Code, string Message);

public record ClientResponse(
    Guid ClientRefId,
    Guid PolicyRefId,
    string Name,
    ClientStatus Status,
    bool IsConnected,
    DateTime? ConnectedAt,
    DateTime? LastActivityAt,
    int? LastRttMs,
    DateTime CreatedAt,
    IReadOnlyList<string> Tags
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
    DateTime CreatedAt,
    IReadOnlyList<string> Tags
);

public record ClientTagsResponse(Guid ClientRefId, IReadOnlyList<string> Tags);

public record ClientTagCountResponse(string Tag, int ClientCount);

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

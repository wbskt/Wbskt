using System.Text.Json;
using Wbskt.Protocol;
using Wbskt.Models;

namespace Wbskt.Management.Models;

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

/// <param name="UpdatingUntil">Set when the device's last disconnect was to install a firmware update: while it is later than now the device counts as updating, not offline. Cleared on its next connect.</param>
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
    IReadOnlyList<string> Tags,
    DateTimeOffset? UpdatingUntil = null
);

/// <param name="System">The platform features the device last said it takes, such as <c>ota</c>; empty when none.</param>
/// <param name="UpdatingUntil">As on <see cref="ClientResponse"/>.</param>
/// <param name="FirmwareVersion">The version of the code the device runs, from its capabilities report, or from the <c>firmware</c> state an older SDK reported; null when it said neither.</param>
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
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> System,
    string? FirmwareVersion = null,
    DateTimeOffset? UpdatingUntil = null
);

public record ClientTagsResponse(Guid ClientRefId, IReadOnlyList<string> Tags);

public record ClientTagCountResponse(string Tag, int ClientCount);

/// <summary>How many clients a workspace or a policy has in each status, and how many are connected now.</summary>
public record ClientCountsResponse(int Total, int Pending, int Registered, int Revoked, int Rejected, int Connected);

/// <summary>A message type a client has sent, for a type picker.</summary>
/// <param name="Count">How many messages of this type have arrived since the platform started counting them.</param>
/// <param name="IsPlatform">A type the platform reads itself (<c>state.report</c>, <c>capabilities</c>), shown apart from the device's own.</param>
/// <param name="SamplePayload">The latest payload of this type, when it was small enough to keep (4,000 characters).</param>
public record ClientMessageTypeResponse(
    string Type,
    long Count,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    bool IsPlatform,
    JsonElement? SamplePayload
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

/// <summary>A client's fields to change; a field left null stays as it is.</summary>
public record UpdateClientRequest(string? Name, ClientStatus? Status);

public record UpdateClientStatusRequest(ClientStatus Status);

public record UpdateClientNameRequest(string Name);

public record SetClientTagsRequest(IReadOnlyList<string>? Tags);

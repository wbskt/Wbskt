using System.Text.Json;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

/// <summary>Reads of a workspace's clients, and the ownership checks other services build on.</summary>
internal sealed class ClientQueryService : IClientQueryService
{
    private readonly IClientProvider _clientProvider;
    private readonly ILogger<ClientQueryService> _logger;

    public ClientQueryService(IClientProvider clientProvider, ILogger<ClientQueryService> logger)
    {
        _clientProvider = clientProvider;
        _logger = logger;
    }

    public async Task<Result<IPagedList<ClientResponse>>> GetAllAsync(int workspaceId, ClientStatus? status, string? name,
        string? tag, int skip, int take, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying clients for WorkspaceId: {WorkspaceId}", workspaceId);

        if (!TryNormalizeFilter(tag, out var normalizedTag))
        {
            return Result<IPagedList<ClientResponse>>.Failure(ClientTags.Invalid);
        }

        var pagedClients = await _clientProvider.GetAllAsync(workspaceId, status, name, normalizedTag, skip, take, cancellationToken);
        _logger.LogTrace("Retrieved {Count} clients for WorkspaceId: {WorkspaceId}", pagedClients.TotalCount, workspaceId);
        
        var result = new PagedList<ClientResponse>(pagedClients.Select(MapToResponse), pagedClients.TotalCount);
        return Result<IPagedList<ClientResponse>>.Success(result);
    }

    public async Task<Result<IPagedList<ClientResponse>>> GetByPolicyIdAsync(int workspaceId, int policyId,
        ClientStatus? status, string? name, string? tag, int skip, int take, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying clients by PolicyId: {PolicyId} in WorkspaceId: {WorkspaceId}", policyId, workspaceId);

        if (!TryNormalizeFilter(tag, out var normalizedTag))
        {
            return Result<IPagedList<ClientResponse>>.Failure(ClientTags.Invalid);
        }

        var pagedClients = await _clientProvider.GetByPolicyIdAsync(workspaceId, policyId, status, name, normalizedTag, skip, take, cancellationToken);
        _logger.LogTrace("Retrieved {Count} clients by PolicyId: {PolicyId}", pagedClients.TotalCount, policyId);
        
        var result = new PagedList<ClientResponse>(pagedClients.Select(MapToResponse), pagedClients.TotalCount);
        return Result<IPagedList<ClientResponse>>.Success(result);
    }

    private Task<Result<ClientDetail>> FindInWorkspaceAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken)
    {
        return WorkspaceOwnership.LoadAsync(workspaceId, () => _clientProvider.FindDetailByRefIdAsync(clientRefId, cancellationToken), WorkspaceOwnership.ClientNotFound);
    }

    public async Task<Result<ClientDetailResponse>> GetDetailAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying client detail for RefId: {ClientRefId} in WorkspaceId: {WorkspaceId}", clientRefId, workspaceId);

        var lookup = await FindInWorkspaceAsync(workspaceId, clientRefId, cancellationToken);
        return lookup.IsSuccess
            ? Result<ClientDetailResponse>.Success(MapToDetailResponse(lookup.Value))
            : Result<ClientDetailResponse>.Failure(lookup.Error);
    }

    public async Task<Result<IReadOnlyList<ClientTagCountResponse>>> GetTagsAsync(int workspaceId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying client tags for WorkspaceId: {WorkspaceId}", workspaceId);

        var tags = await _clientProvider.GetTagsAsync(workspaceId, cancellationToken);
        IReadOnlyList<ClientTagCountResponse> response = tags.Select(t => new ClientTagCountResponse(t.Tag, t.ClientCount)).ToList();
        return Result<IReadOnlyList<ClientTagCountResponse>>.Success(response);
    }

    // No filter stays no filter; a filter that could never match a stored tag is refused.
    private static bool TryNormalizeFilter(string? tag, out string? normalized)
    {
        normalized = null;
        if (tag is null)
        {
            return true;
        }

        if (!ClientTags.TryNormalize(tag, out var value))
        {
            return false;
        }

        normalized = value;
        return true;
    }

    public async Task<Result<IReadOnlyCollection<ClientStateVariableResponse>>> GetStateAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying state variables for client RefId {ClientRefId} in WorkspaceId: {WorkspaceId}", clientRefId, workspaceId);

        var lookup = await FindInWorkspaceAsync(workspaceId, clientRefId, cancellationToken);
        if (lookup.IsFailure)
        {
            return Result<IReadOnlyCollection<ClientStateVariableResponse>>.Failure(lookup.Error);
        }

        var variables = await _clientProvider.GetStateVariablesAsync(lookup.Value.Id, cancellationToken);
        IReadOnlyCollection<ClientStateVariableResponse> response = variables
            .Select(v => new ClientStateVariableResponse(v.Name, v.DataType, v.ValueJson, v.UpdatedAt))
            .ToList()
            .AsReadOnly();

        return Result<IReadOnlyCollection<ClientStateVariableResponse>>.Success(response);
    }

    public async Task<Result<int>> EnsureClientInWorkspaceAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default)
    {
        var clientResult = await FindInWorkspaceAsync(workspaceId, clientRefId, cancellationToken);
        return clientResult.IsSuccess
            ? Result<int>.Success(clientResult.Value.Id)
            : Result<int>.Failure(clientResult.Error);
    }

    public async Task<Result<ClientCommandTarget>> ResolveCommandTargetAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default)
    {
        var clientResult = await FindInWorkspaceAsync(workspaceId, clientRefId, cancellationToken);
        if (clientResult.IsFailure)
        {
            return Result<ClientCommandTarget>.Failure(clientResult.Error);
        }

        var client = clientResult.Value;
        if (!client.IsConnected || string.IsNullOrEmpty(client.ConnectedHostId))
        {
            _logger.LogInformation("Command refused: client RefId {ClientRefId} is offline", clientRefId);
            return Result<ClientCommandTarget>.Failure(DeviceOffline);
        }

        return Result<ClientCommandTarget>.Success(new ClientCommandTarget(client.Id, client.ConnectedHostId));
    }

    private static readonly Error DeviceOffline = Error.Conflict("DEVICE_OFFLINE", "The device is offline. Commands are only delivered to connected devices.");

    private static ClientResponse MapToResponse(Client c)
    {
        return new ClientResponse(
            c.RefId,
            c.PolicyRefId,
            c.Name,
            c.Status,
            c.IsConnected,
            c.ConnectedAt,
            c.LastActivityAt,
            c.LastRttMs,
            c.CreatedAt,
            c.Tags
        );
    }

    /// <summary>Capabilities are stored as the device reported them, so their casing is not ours to rely on.</summary>
    private static readonly JsonSerializerOptions CapabilitiesJson = new() { PropertyNameCaseInsensitive = true };

    private static ClientDetailResponse MapToDetailResponse(ClientDetail d)
    {
        IReadOnlyList<CommandCapability>? capabilities = null;
        if (!string.IsNullOrEmpty(d.CapabilitiesJson))
        {
            try
            {
                capabilities = JsonSerializer.Deserialize<List<CommandCapability>>(d.CapabilitiesJson, CapabilitiesJson);
            }
            catch (JsonException)
            {
                // Stored blob is unreadable; surface the client without capabilities rather than failing the page.
            }
        }

        return new ClientDetailResponse(
            d.RefId,
            d.PolicyRefId,
            d.PolicyName,
            d.Name,
            d.Status,
            d.IsConnected,
            d.ConnectedAt,
            d.LastActivityAt,
            d.LastRttMs,
            d.RttMeasuredAt,
            d.AgentName,
            d.AgentVersion,
            d.Platform,
            capabilities,
            d.CreatedAt,
            d.Tags
        );
    }
}

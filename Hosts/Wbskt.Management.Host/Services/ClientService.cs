using System.Text.Json;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

internal sealed class ClientService : IClientService
{
    private readonly IClientProvider _clientProvider;
    private readonly IRegistrationPolicyProvider _policyProvider;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ClientService> _logger;

    public ClientService(
        IClientProvider clientProvider, 
        IRegistrationPolicyProvider policyProvider,
        IEventBus eventBus,
        ILogger<ClientService> logger)
    {
        _clientProvider = clientProvider;
        _policyProvider = policyProvider;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<Result<IPagedList<ClientResponse>>> GetAllAsync(int workspaceId, ClientStatus? status, string? name,
        int skip, int take, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying clients for WorkspaceId: {WorkspaceId}", workspaceId);

        try
        {
            var pagedClients = await _clientProvider.GetAllAsync(workspaceId, status, name, skip, take, cancellationToken);
            _logger.LogTrace("Retrieved {Count} clients for WorkspaceId: {WorkspaceId}", pagedClients.TotalCount, workspaceId);
            
            var result = new PagedList<ClientResponse>(pagedClients.Select(MapToResponse), pagedClients.TotalCount);
            return Result<IPagedList<ClientResponse>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to query clients for WorkspaceId: {WorkspaceId}. Error: {Message}", workspaceId, ex.Message);
            _logger.LogTrace(ex, "GetAllAsync exception stack trace for WorkspaceId {WorkspaceId}", workspaceId);
            return Result<IPagedList<ClientResponse>>.Failure(Error.Failure("CLIENT_QUERY_ERROR", ex.Message));
        }
    }

    public async Task<Result<IPagedList<ClientResponse>>> GetByPolicyIdAsync(int workspaceId, int policyId,
        ClientStatus? status, string? name, int skip, int take, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying clients by PolicyId: {PolicyId} in WorkspaceId: {WorkspaceId}", policyId, workspaceId);

        try
        {
            var pagedClients = await _clientProvider.GetByPolicyIdAsync(workspaceId, policyId, status, name, skip, take, cancellationToken);
            _logger.LogTrace("Retrieved {Count} clients by PolicyId: {PolicyId}", pagedClients.TotalCount, policyId);
            
            var result = new PagedList<ClientResponse>(pagedClients.Select(MapToResponse), pagedClients.TotalCount);
            return Result<IPagedList<ClientResponse>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to query clients by PolicyId: {PolicyId}. Error: {Message}", policyId, ex.Message);
            _logger.LogTrace(ex, "GetByPolicyIdAsync exception stack trace for PolicyId {PolicyId}", policyId);
            return Result<IPagedList<ClientResponse>>.Failure(Error.Failure("CLIENT_QUERY_ERROR", ex.Message));
        }
    }

    public async Task<Result> UpdateStatusAsync(int workspaceId, int id, ClientStatus status, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating client ID {ClientId} status to '{ClientStatus}' in WorkspaceId: {WorkspaceId}", id, status, workspaceId);

        try
        {
            Client client;
            try
            {
                client = await _clientProvider.GetByIdAsync(id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to update client status: Client ID {ClientId} not found. Error: {Message}", id, ex.Message);
                _logger.LogTrace(ex, "GetByIdAsync lookup failure stack trace for ClientId {ClientId}", id);
                return Result.Failure(Error.NotFound("CLIENT_NOT_FOUND", "Client not found."));
            }

            if (client.WorkspaceId != workspaceId)
            {
                _logger.LogWarning("Client status update rejected: Client ID {ClientId} does not belong to WorkspaceId: {WorkspaceId}", id, workspaceId);
                return Result.Failure(Error.Forbidden("CLIENT_UNAUTHORIZED", "Client does not belong to this workspace."));
            }
            
            var oldStatus = client.Status;
            if (oldStatus == status)
            {
                _logger.LogDebug("Client ID {ClientId} is already in status '{ClientStatus}'. Skipping update.", id, status);
                return Result.Success();
            }

            RegistrationPolicy policy;
            try
            {
                policy = await _policyProvider.GetByRefIdAsync(client.PolicyRefId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve policy reference: '{PolicyRefId}' for client ID: {ClientId}", client.PolicyRefId, id);
                return Result.Failure(Error.Failure("POLICY_RESOLVE_ERROR", "Failed to resolve policy associated with the client."));
            }

            if (status == ClientStatus.Registered)
            {
                if (policy.MaxClients.HasValue)
                {
                    var currentCount = await _clientProvider.GetRegisteredCountByPolicyIdAsync(policy.Id, cancellationToken);
                    if (currentCount >= policy.MaxClients.Value)
                    {
                        _logger.LogWarning("Client approval failed: Policy registration limit reached for Policy ID {PolicyId}", policy.Id);
                        return Result.Failure(Error.Validation("POLICY_LIMIT_REACHED", "Policy registration limit reached. Cannot approve more clients."));
                    }
                }
            }

            await _clientProvider.UpdateStatusAsync(id, status, cancellationToken);
            _logger.LogInformation("Successfully updated client ID {ClientId} status from '{OldStatus}' to '{NewStatus}'", id, oldStatus, status);

            await _eventBus.PublishAsync(new ClientStatusChangedEvent(client.RefId, client.Id, policy.RefId, policy.Id, client.WorkspaceId, (byte)status), cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error updating client ID {ClientId} status. Error: {Message}", id, ex.Message);
            _logger.LogTrace(ex, "UpdateStatusAsync exception stack trace for ClientId {ClientId}", id);
            return Result.Failure(Error.Failure("CLIENT_UPDATE_ERROR", ex.Message));
        }
    }

    public async Task<Result<ClientDetailResponse>> GetDetailAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying client detail for RefId: {ClientRefId} in WorkspaceId: {WorkspaceId}", clientRefId, workspaceId);

        try
        {
            ClientDetail detail;
            try
            {
                detail = await _clientProvider.GetDetailByRefIdAsync(clientRefId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Client detail lookup failed for RefId: {ClientRefId}. Error: {Message}", clientRefId, ex.Message);
                return Result<ClientDetailResponse>.Failure(Error.NotFound("CLIENT_NOT_FOUND", "Client not found."));
            }

            if (detail.WorkspaceId != workspaceId)
            {
                _logger.LogWarning("Client detail rejected: RefId {ClientRefId} does not belong to WorkspaceId: {WorkspaceId}", clientRefId, workspaceId);
                return Result<ClientDetailResponse>.Failure(Error.Forbidden("CLIENT_UNAUTHORIZED", "Client does not belong to this workspace."));
            }

            return Result<ClientDetailResponse>.Success(MapToDetailResponse(detail));
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to query client detail for RefId: {ClientRefId}. Error: {Message}", clientRefId, ex.Message);
            _logger.LogTrace(ex, "GetDetailAsync exception stack trace for ClientRefId {ClientRefId}", clientRefId);
            return Result<ClientDetailResponse>.Failure(Error.Failure("CLIENT_QUERY_ERROR", ex.Message));
        }
    }

    public async Task<Result> RenameAsync(int workspaceId, int id, string name, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Renaming client ID {ClientId} in WorkspaceId: {WorkspaceId}", id, workspaceId);

        try
        {
            Client client;
            try
            {
                client = await _clientProvider.GetByIdAsync(id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to rename client: Client ID {ClientId} not found. Error: {Message}", id, ex.Message);
                return Result.Failure(Error.NotFound("CLIENT_NOT_FOUND", "Client not found."));
            }

            if (client.WorkspaceId != workspaceId)
            {
                _logger.LogWarning("Client rename rejected: Client ID {ClientId} does not belong to WorkspaceId: {WorkspaceId}", id, workspaceId);
                return Result.Failure(Error.Forbidden("CLIENT_UNAUTHORIZED", "Client does not belong to this workspace."));
            }

            var oldName = client.Name;
            if (oldName == name)
            {
                _logger.LogDebug("Client ID {ClientId} is already named '{Name}'. Skipping update.", id, name);
                return Result.Success();
            }

            await _clientProvider.UpdateNameAsync(id, name, cancellationToken);
            _logger.LogInformation("Successfully renamed client ID {ClientId} from '{OldName}' to '{NewName}'", id, oldName, name);

            await _eventBus.PublishAsync(new ClientRenamedEvent(client.RefId, client.Id, client.WorkspaceId, oldName, name), cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error renaming client ID {ClientId}. Error: {Message}", id, ex.Message);
            _logger.LogTrace(ex, "RenameAsync exception stack trace for ClientId {ClientId}", id);
            return Result.Failure(Error.Failure("CLIENT_UPDATE_ERROR", ex.Message));
        }
    }

    public async Task<Result<IReadOnlyCollection<ClientStateVariableResponse>>> GetStateAsync(int workspaceId, int id, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying state variables for client ID {ClientId} in WorkspaceId: {WorkspaceId}", id, workspaceId);

        try
        {
            Client client;
            try
            {
                client = await _clientProvider.GetByIdAsync(id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("State query failed: Client ID {ClientId} not found. Error: {Message}", id, ex.Message);
                return Result<IReadOnlyCollection<ClientStateVariableResponse>>.Failure(Error.NotFound("CLIENT_NOT_FOUND", "Client not found."));
            }

            if (client.WorkspaceId != workspaceId)
            {
                _logger.LogWarning("State query rejected: Client ID {ClientId} does not belong to WorkspaceId: {WorkspaceId}", id, workspaceId);
                return Result<IReadOnlyCollection<ClientStateVariableResponse>>.Failure(Error.Forbidden("CLIENT_UNAUTHORIZED", "Client does not belong to this workspace."));
            }

            var variables = await _clientProvider.GetStateVariablesAsync(id, cancellationToken);
            IReadOnlyCollection<ClientStateVariableResponse> response = variables
                .Select(v => new ClientStateVariableResponse(v.Name, v.DataType, v.ValueJson, v.UpdatedAt))
                .ToList()
                .AsReadOnly();

            return Result<IReadOnlyCollection<ClientStateVariableResponse>>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to query state variables for client ID {ClientId}. Error: {Message}", id, ex.Message);
            _logger.LogTrace(ex, "GetStateAsync exception stack trace for ClientId {ClientId}", id);
            return Result<IReadOnlyCollection<ClientStateVariableResponse>>.Failure(Error.Failure("CLIENT_QUERY_ERROR", ex.Message));
        }
    }

    public async Task<Result<int>> EnsureClientInWorkspaceAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Verifying client RefId: {ClientRefId} belongs to WorkspaceId: {WorkspaceId}", clientRefId, workspaceId);

        Client client;
        try
        {
            client = await _clientProvider.GetDetailByRefIdAsync(clientRefId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Client membership check failed: RefId {ClientRefId} not found. Error: {Message}", clientRefId, ex.Message);
            _logger.LogTrace(ex, "EnsureClientInWorkspaceAsync lookup failure stack trace for {ClientRefId}", clientRefId);
            return Result<int>.Failure(Error.NotFound("CLIENT_NOT_FOUND", "Client not found."));
        }

        if (client.WorkspaceId != workspaceId)
        {
            _logger.LogWarning("Client membership rejected: RefId {ClientRefId} does not belong to WorkspaceId: {WorkspaceId}", clientRefId, workspaceId);
            return Result<int>.Failure(Error.Forbidden("CLIENT_UNAUTHORIZED", "Client does not belong to this workspace."));
        }

        return Result<int>.Success(client.Id);
    }

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
            c.CreatedAt
        );
    }

    private static ClientDetailResponse MapToDetailResponse(ClientDetail d)
    {
        IReadOnlyList<CommandCapability>? capabilities = null;
        if (!string.IsNullOrEmpty(d.CapabilitiesJson))
        {
            try
            {
                capabilities = JsonSerializer.Deserialize<List<CommandCapability>>(
                    d.CapabilitiesJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
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
            d.CreatedAt
        );
    }
}

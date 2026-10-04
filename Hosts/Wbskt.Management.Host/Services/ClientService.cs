using System.Text.Json;
using Microsoft.Data.SqlClient;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Client;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Models;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Management.Host.Services;

internal sealed class ClientService : IClientService
{
    // THROW number from dbo.Client_UpdateStatus (and dbo.Client_Create) when a policy is full.
    private const int PolicyLimitReached = 50020;

    private readonly IClientProvider _clientProvider;
    private readonly IRegistrationPolicyProvider _policyProvider;
    private readonly IEventBus _eventBus;
    private readonly IClientTokenCutoffs _cutoffs;
    private readonly ILogger<ClientService> _logger;

    public ClientService(
        IClientProvider clientProvider, 
        IRegistrationPolicyProvider policyProvider,
        IEventBus eventBus,
        IClientTokenCutoffs cutoffs,
        ILogger<ClientService> logger)
    {
        _clientProvider = clientProvider;
        _policyProvider = policyProvider;
        _eventBus = eventBus;
        _cutoffs = cutoffs;
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

            try
            {
                await _clientProvider.UpdateStatusAsync(id, status, cancellationToken);
            }
            catch (SqlException ex) when (ex.Number == PolicyLimitReached)
            {
                // The check above is the friendly early answer; this one, under a lock on the policy,
                // is the one that holds when approvals race.
                _logger.LogWarning("Client approval failed: Policy registration limit reached for Policy ID {PolicyId}", policy.Id);
                return Result.Failure(Error.Validation("POLICY_LIMIT_REACHED", "Policy registration limit reached. Cannot approve more clients."));
            }

            _logger.LogInformation("Successfully updated client ID {ClientId} status from '{OldStatus}' to '{NewStatus}'", id, oldStatus, status);

            if (status == ClientStatus.Registered)
            {
                await _cutoffs.ReinstateAsync(client.RefId, DateTime.UtcNow);
            }
            else if (oldStatus == ClientStatus.Registered)
            {
                await _cutoffs.RevokeAsync(client.RefId);
            }

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

    public async Task<Result<BulkClientStatusResponse>> UpdateStatusesAsync(int workspaceId, IReadOnlyList<Guid> clientRefIds,
        ClientStatus status, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating {Count} clients to status '{ClientStatus}' in WorkspaceId: {WorkspaceId}", clientRefIds.Count, status, workspaceId);

        var updated = new List<Guid>();
        var failed = new List<BulkClientStatusFailure>();

        // One at a time, in the order given: approvals compete for the policy's remaining places, and
        // a predictable order decides which ones get them.
        foreach (var clientRefId in clientRefIds.Distinct())
        {
            var idResult = await EnsureClientInWorkspaceAsync(workspaceId, clientRefId, cancellationToken);
            var result = idResult.IsSuccess
                ? await UpdateStatusAsync(workspaceId, idResult.Value, status, cancellationToken)
                : Result.Failure(idResult.Error);

            if (result.IsSuccess)
            {
                updated.Add(clientRefId);
            }
            else
            {
                failed.Add(new BulkClientStatusFailure(clientRefId, result.Error.Code, result.Error.Message));
            }
        }

        return Result<BulkClientStatusResponse>.Success(new BulkClientStatusResponse(updated, failed));
    }

    public async Task<Result> DeleteAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting client RefId {ClientRefId} in WorkspaceId: {WorkspaceId}", clientRefId, workspaceId);

        try
        {
            var client = await FindInWorkspaceAsync(workspaceId, clientRefId, cancellationToken);
            if (client is null)
            {
                return Result.Failure(Unauthorized);
            }

            if (!await _clientProvider.DeleteAsync(client.Id, workspaceId, cancellationToken))
            {
                // Deleted by someone else between the lookup and here: the outcome they asked for.
                return Result.Success();
            }

            _logger.LogInformation("Deleted client ID {ClientId} ('{Name}') from WorkspaceId: {WorkspaceId}", client.Id, client.Name, workspaceId);
            await _cutoffs.RevokeAsync(client.RefId);
            await _eventBus.PublishAsync(
                new ClientDeletedEvent(client.RefId, client.Id, client.PolicyRefId, client.PolicyId, workspaceId, client.Name),
                cancellationToken);

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error deleting client RefId {ClientRefId}. Error: {Message}", clientRefId, ex.Message);
            _logger.LogTrace(ex, "DeleteAsync exception stack trace for ClientRefId {ClientRefId}", clientRefId);
            return Result.Failure(Error.Failure("CLIENT_DELETE_ERROR", ex.Message));
        }
    }

    public async Task<Result<ClientSecretResponse>> RotateSecretAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Rotating secret for client RefId {ClientRefId} in WorkspaceId: {WorkspaceId}", clientRefId, workspaceId);

        try
        {
            var client = await FindInWorkspaceAsync(workspaceId, clientRefId, cancellationToken);
            if (client is null)
            {
                return Result<ClientSecretResponse>.Failure(Unauthorized);
            }

            var secret = ClientSecrets.Generate();
            if (!await _clientProvider.UpdateSecretAsync(client.Id, workspaceId, ClientSecrets.Hash(secret), cancellationToken))
            {
                return Result<ClientSecretResponse>.Failure(Unauthorized);
            }

            _logger.LogInformation("Rotated secret for client ID {ClientId}", client.Id);
            var rotatedAt = DateTime.UtcNow;
            await _cutoffs.RevokeIssuedBeforeAsync(client.RefId, rotatedAt);
            await _eventBus.PublishAsync(new ClientSecretRotatedEvent(client.RefId, client.Id, workspaceId, rotatedAt), cancellationToken);

            return Result<ClientSecretResponse>.Success(new ClientSecretResponse(client.RefId, secret));
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error rotating secret for client RefId {ClientRefId}. Error: {Message}", clientRefId, ex.Message);
            _logger.LogTrace(ex, "RotateSecretAsync exception stack trace for ClientRefId {ClientRefId}", clientRefId);
            return Result<ClientSecretResponse>.Failure(Error.Failure("CLIENT_UPDATE_ERROR", ex.Message));
        }
    }

    // The client, or null both when the reference resolves to nothing and when it is another
    // workspace's; see Unauthorized for why the two are not told apart.
    private async Task<Client?> FindInWorkspaceAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken)
    {
        Client client;
        try
        {
            client = await _clientProvider.GetDetailByRefIdAsync(clientRefId, cancellationToken);
        }
        catch (NotFoundException)
        {
            return null;
        }

        return client.WorkspaceId == workspaceId ? client : null;
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
        var clientResult = await FindDetailInWorkspaceAsync(workspaceId, clientRefId, cancellationToken);
        return clientResult.IsSuccess
            ? Result<int>.Success(clientResult.Value.Id)
            : Result<int>.Failure(clientResult.Error);
    }

    public async Task<Result<ClientCommandTarget>> ResolveCommandTargetAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default)
    {
        var clientResult = await FindDetailInWorkspaceAsync(workspaceId, clientRefId, cancellationToken);
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

    private async Task<Result<ClientDetail>> FindDetailInWorkspaceAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Verifying client RefId: {ClientRefId} belongs to WorkspaceId: {WorkspaceId}", clientRefId, workspaceId);

        ClientDetail client;
        try
        {
            client = await _clientProvider.GetDetailByRefIdAsync(clientRefId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Client membership check failed: RefId {ClientRefId} not found. Error: {Message}", clientRefId, ex.Message);
            _logger.LogTrace(ex, "EnsureClientInWorkspaceAsync lookup failure stack trace for {ClientRefId}", clientRefId);
            return Result<ClientDetail>.Failure(Unauthorized);
        }

        if (client.WorkspaceId != workspaceId)
        {
            _logger.LogWarning("Client membership rejected: RefId {ClientRefId} does not belong to WorkspaceId: {WorkspaceId}", clientRefId, workspaceId);
            return Result<ClientDetail>.Failure(Unauthorized);
        }

        return Result<ClientDetail>.Success(client);
    }

    private static readonly Error DeviceOffline = Error.Conflict("DEVICE_OFFLINE", "The device is offline. Commands are only delivered to connected devices.");

    /// <summary>
    /// One answer for both "no such client" and "not this workspace's client". This gates the
    /// command and ping endpoints, which is precisely where a caller would probe a reference it
    /// guessed or kept from a workspace it was removed from — distinguishing the two would confirm
    /// that a reference names a real client somewhere. Per "The ID Boundary" in
    /// Docs/Coding.Conventions.md, an unresolvable reference is a permission answer, not a 404.
    /// </summary>
    private static readonly Error Unauthorized = Error.Forbidden("CLIENT_UNAUTHORIZED", "Client not found in this workspace.");

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

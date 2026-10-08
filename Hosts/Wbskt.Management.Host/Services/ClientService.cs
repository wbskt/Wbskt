using System.Text.Json;
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
    private readonly IClientProvider _clientProvider;
    private readonly IEventBus _eventBus;
    private readonly IClientTokenCutoffs _cutoffs;
    private readonly ILogger<ClientService> _logger;

    public ClientService(
        IClientProvider clientProvider,
        IEventBus eventBus,
        IClientTokenCutoffs cutoffs,
        ILogger<ClientService> logger)
    {
        _clientProvider = clientProvider;
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

    public async Task<Result> UpdateStatusAsync(int workspaceId, Guid clientRefId, ClientStatus status, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating client RefId {ClientRefId} status to '{ClientStatus}' in WorkspaceId: {WorkspaceId}", clientRefId, status, workspaceId);

        try
        {
            var change = (await _clientProvider.UpdateStatusesAsync(workspaceId, [clientRefId], status, cancellationToken)).Single();
            switch (change.Outcome)
            {
                case ClientStatusOutcome.NotFound:
                    _logger.LogWarning("Failed to update client status: RefId {ClientRefId} not found", clientRefId);
                    return Result.Failure(Error.NotFound("CLIENT_NOT_FOUND", "Client not found."));
                case ClientStatusOutcome.OtherWorkspace:
                    _logger.LogWarning("Client status update rejected: RefId {ClientRefId} does not belong to WorkspaceId: {WorkspaceId}", clientRefId, workspaceId);
                    return Result.Failure(Error.Forbidden("CLIENT_UNAUTHORIZED", "Client does not belong to this workspace."));
                case ClientStatusOutcome.PolicyLimitReached:
                    _logger.LogWarning("Client approval failed: Policy registration limit reached for Policy ID {PolicyId}", change.PolicyId);
                    return Result.Failure(PolicyFull);
                case ClientStatusOutcome.Updated:
                    await AnnounceStatusChangeAsync(workspaceId, change, status, cancellationToken);
                    break;
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error updating client RefId {ClientRefId} status. Error: {Message}", clientRefId, ex.Message);
            _logger.LogTrace(ex, "UpdateStatusAsync exception stack trace for ClientRefId {ClientRefId}", clientRefId);
            return Result.Failure(Error.Failure("CLIENT_UPDATE_ERROR", ex.Message));
        }
    }

    public async Task<Result<BulkClientStatusResponse>> UpdateStatusesAsync(int workspaceId, IReadOnlyList<Guid> clientRefIds,
        ClientStatus status, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating {Count} clients to status '{ClientStatus}' in WorkspaceId: {WorkspaceId}", clientRefIds.Count, status, workspaceId);

        // One database call for the whole list, in the order given: approvals compete for the
        // policy's remaining places, and a predictable order decides which ones get them.
        IReadOnlyList<ClientStatusChange> changes;
        try
        {
            changes = await _clientProvider.UpdateStatusesAsync(workspaceId, clientRefIds.Distinct().ToList(), status, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error updating {Count} clients' status. Error: {Message}", clientRefIds.Count, ex.Message);
            _logger.LogTrace(ex, "UpdateStatusesAsync exception stack trace");
            return Result<BulkClientStatusResponse>.Failure(Error.Failure("CLIENT_UPDATE_ERROR", ex.Message));
        }

        var updated = new List<Guid>();
        var failed = new List<BulkClientStatusFailure>();
        foreach (var change in changes)
        {
            Error? error = change.Outcome switch
            {
                // A missing client and another workspace's read the same; see Unauthorized.
                ClientStatusOutcome.NotFound or ClientStatusOutcome.OtherWorkspace => Unauthorized,
                ClientStatusOutcome.PolicyLimitReached => PolicyFull,
                _ => null
            };

            if (error is null && change.Outcome == ClientStatusOutcome.Updated)
            {
                try
                {
                    await AnnounceStatusChangeAsync(workspaceId, change, status, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Client RefId {ClientRefId} changed status but announcing it failed. Error: {Message}", change.RefId, ex.Message);
                    _logger.LogTrace(ex, "AnnounceStatusChangeAsync exception stack trace for ClientRefId {ClientRefId}", change.RefId);
                    error = Error.Failure("CLIENT_UPDATE_ERROR", ex.Message);
                }
            }

            if (error is null)
            {
                updated.Add(change.RefId);
            }
            else
            {
                failed.Add(new BulkClientStatusFailure(change.RefId, error.Code, error.Message));
            }
        }

        return Result<BulkClientStatusResponse>.Success(new BulkClientStatusResponse(updated, failed));
    }

    // Token cutoffs first, so a revoked device is refused before anyone hears it was revoked.
    private async Task AnnounceStatusChangeAsync(int workspaceId, ClientStatusChange change, ClientStatus status, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updated client ID {ClientId} status from '{OldStatus}' to '{NewStatus}'", change.Id, change.OldStatus, status);

        if (status == ClientStatus.Registered)
        {
            await _cutoffs.ReinstateAsync(change.RefId, DateTime.UtcNow);
        }
        else if (change.OldStatus == ClientStatus.Registered)
        {
            await _cutoffs.RevokeAsync(change.RefId);
        }

        await _eventBus.PublishAsync(
            new ClientStatusChangedEvent(change.RefId, change.Id, change.PolicyRefId, change.PolicyId, workspaceId, (byte)status),
            cancellationToken);
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

    public async Task<Result> RenameAsync(int workspaceId, Guid clientRefId, string name, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Renaming client RefId {ClientRefId} in WorkspaceId: {WorkspaceId}", clientRefId, workspaceId);

        try
        {
            var lookup = await GetInWorkspaceAsync(workspaceId, clientRefId, cancellationToken);
            if (lookup.IsFailure)
            {
                return Result.Failure(lookup.Error);
            }

            var client = lookup.Value;
            var id = client.Id;

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
            _logger.LogError("Unexpected error renaming client RefId {ClientRefId}. Error: {Message}", clientRefId, ex.Message);
            _logger.LogTrace(ex, "RenameAsync exception stack trace for ClientRefId {ClientRefId}", clientRefId);
            return Result.Failure(Error.Failure("CLIENT_UPDATE_ERROR", ex.Message));
        }
    }

    public async Task<Result<IReadOnlyCollection<ClientStateVariableResponse>>> GetStateAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying state variables for client RefId {ClientRefId} in WorkspaceId: {WorkspaceId}", clientRefId, workspaceId);

        try
        {
            var lookup = await GetInWorkspaceAsync(workspaceId, clientRefId, cancellationToken);
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
        catch (Exception ex)
        {
            _logger.LogError("Failed to query state variables for client RefId {ClientRefId}. Error: {Message}", clientRefId, ex.Message);
            _logger.LogTrace(ex, "GetStateAsync exception stack trace for ClientRefId {ClientRefId}", clientRefId);
            return Result<IReadOnlyCollection<ClientStateVariableResponse>>.Failure(Error.Failure("CLIENT_QUERY_ERROR", ex.Message));
        }
    }

    // One read for both the lookup and the workspace check, for the console endpoints that tell a
    // missing client (404) from another workspace's (403), as they always have.
    private async Task<Result<ClientDetail>> GetInWorkspaceAsync(int workspaceId, Guid clientRefId, CancellationToken cancellationToken)
    {
        ClientDetail client;
        try
        {
            client = await _clientProvider.GetDetailByRefIdAsync(clientRefId, cancellationToken);
        }
        catch (NotFoundException)
        {
            _logger.LogWarning("Client RefId {ClientRefId} not found", clientRefId);
            return Result<ClientDetail>.Failure(Error.NotFound("CLIENT_NOT_FOUND", "Client not found."));
        }

        if (client.WorkspaceId != workspaceId)
        {
            _logger.LogWarning("Client RefId {ClientRefId} does not belong to WorkspaceId: {WorkspaceId}", clientRefId, workspaceId);
            return Result<ClientDetail>.Failure(Error.Forbidden("CLIENT_UNAUTHORIZED", "Client does not belong to this workspace."));
        }

        return Result<ClientDetail>.Success(client);
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

    private static readonly Error PolicyFull = Error.Validation("POLICY_LIMIT_REACHED", "Policy registration limit reached. Cannot approve more clients.");

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

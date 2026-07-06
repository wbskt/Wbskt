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
                return Result.Failure(Error.Unauthorized("CLIENT_UNAUTHORIZED", "Client does not belong to this workspace."));
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

    private static ClientResponse MapToResponse(Client c)
    {
        return new ClientResponse(
            c.RefId,
            c.PolicyRefId,
            c.Name,
            c.Status,
            c.IsConnected,
            c.LastActivityAt,
            c.CreatedAt
        );
    }
}

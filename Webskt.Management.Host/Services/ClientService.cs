using Webskt.Common.Abstraction.Exceptions;
using Webskt.Common.Abstraction.Models;
using Webskt.Common.Abstraction.Models.Management;
using Webskt.EventBus.Abstractions;
using Webskt.Events.Shared;
using Webskt.Management.Host.Models;
using Webskt.Management.Host.Providers;

namespace Webskt.Management.Host.Services;

internal sealed class ClientService : IClientService
{
    private readonly IClientProvider _clientProvider;
    private readonly IRegistrationPolicyProvider _policyProvider;
    private readonly IEventBus _eventBus;

    public ClientService(
        IClientProvider clientProvider, 
        IRegistrationPolicyProvider policyProvider,
        IEventBus eventBus)
    {
        _clientProvider = clientProvider;
        _policyProvider = policyProvider;
        _eventBus = eventBus;
    }

    public async Task<IPagedList<ClientResponse>> GetAllAsync(ClientStatus? status, string? name, int skip, int take, CancellationToken cancellationToken = default)
    {
        var pagedClients = await _clientProvider.GetAllAsync(status, name, skip, take, cancellationToken);
        
        return new PagedList<ClientResponse>(pagedClients.Select(MapToResponse), pagedClients.TotalCount);
    }

    public async Task<IPagedList<ClientResponse>> GetByPolicyIdAsync(int policyId, ClientStatus? status, string? name, int skip, int take, CancellationToken cancellationToken = default)
    {
        var pagedClients = await _clientProvider.GetByPolicyIdAsync(policyId, status, name, skip, take, cancellationToken);

        return new PagedList<ClientResponse>(pagedClients.Select(MapToResponse), pagedClients.TotalCount);
    }

    public async Task UpdateStatusAsync(int workspaceId, int id, ClientStatus status, CancellationToken cancellationToken = default)
    {
        var client = await _clientProvider.GetByIdAsync(id, cancellationToken);
        if (client.WorkspaceId != workspaceId)
        {
            throw new SecurityException("Client does not belong to this workspace.");
        }
        
        var oldStatus = client.Status;

        if (oldStatus == status)
        {
            return;
        }

        if (status == ClientStatus.Registered)
        {
            var policy = await _policyProvider.GetByRefIdAsync(client.PolicyRefId, cancellationToken);

            if (policy.MaxClients.HasValue)
            {
                var currentCount = await _clientProvider.GetRegisteredCountByPolicyIdAsync(policy.Id, cancellationToken);

                if (currentCount >= policy.MaxClients.Value)
                {
                    throw new ValidationException("Policy registration limit reached. Cannot approve more clients.");
                }
            }
        }

        await _clientProvider.UpdateStatusAsync(id, status, cancellationToken);

        await _eventBus.PublishAsync(new ClientStatusChangedEvent(client.RefId, client.WorkspaceId, oldStatus, status), cancellationToken);
    }

    private static ClientResponse MapToResponse(Client c)
    {
        return new ClientResponse(
            c.RefId,
            c.PolicyRefId,
            c.Name,
            c.Status,
            c.CreatedAt
        );
    }
}

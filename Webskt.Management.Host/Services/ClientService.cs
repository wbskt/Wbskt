using Webskt.Management.Host.Models;
using Webskt.Management.Host.Providers;

namespace Webskt.Management.Host.Services;

public class ClientService : IClientService
{
    private readonly IClientProvider _clientProvider;
    private readonly IRegistrationPolicyProvider _policyProvider;

    public ClientService(IClientProvider clientProvider, IRegistrationPolicyProvider policyProvider)
    {
        _clientProvider = clientProvider;
        _policyProvider = policyProvider;
    }

    public async Task<IReadOnlyCollection<ClientResponse>> GetAllAsync()
    {
        var clients = await _clientProvider.GetAllAsync();
        
        return clients.Select(MapToResponse).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyCollection<ClientResponse>> GetByPolicyRefIdAsync(Guid policyRefId)
    {
        // 1. Resolve internal ID
        int policyId = await _policyProvider.FindByRefIdAsync(policyRefId);

        // 2. Fetch clients
        var clients = await _clientProvider.GetByPolicyIdAsync(policyId);

        return clients.Select(MapToResponse).ToList().AsReadOnly();
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

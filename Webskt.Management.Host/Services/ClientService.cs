using Webskt.Management.Host.Models;
using Webskt.Management.Host.Providers;

namespace Webskt.Management.Host.Services;

public class ClientService : IClientService
{
    private readonly IClientProvider _clientProvider;

    public ClientService(IClientProvider clientProvider)
    {
        _clientProvider = clientProvider;
    }

    public async Task<IReadOnlyCollection<ClientResponse>> GetAllAsync()
    {
        var clients = await _clientProvider.GetAllAsync();
        
        return clients.Select(MapToResponse).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyCollection<ClientResponse>> GetByPolicyIdAsync(int policyId)
    {
        // Fetch clients
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

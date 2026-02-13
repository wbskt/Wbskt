using Webskt.Common.Abstraction.Exceptions;
using Webskt.Common.Abstraction.Models.Management;
using Webskt.Management.Host.Models;
using Webskt.Management.Host.Providers;

namespace Webskt.Management.Host.Services;

internal sealed class ClientService : IClientService
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

    public async Task<IReadOnlyCollection<ClientResponse>> GetByPolicyIdAsync(int policyId)
    {
        // Fetch clients
        var clients = await _clientProvider.GetByPolicyIdAsync(policyId);

        return clients.Select(MapToResponse).ToList().AsReadOnly();
    }

    public async Task UpdateStatusAsync(int id, ClientStatus status)
    {
        var client = await _clientProvider.GetByIdAsync(id);

        if (client.Status == status)
        {
            return;
        }

        if (status == ClientStatus.Registered)
        {
            var policy = await _policyProvider.GetByRefIdAsync(client.PolicyRefId);

            if (policy.MaxClients.HasValue)
            {
                var currentCount = await _clientProvider.GetRegisteredCountByPolicyIdAsync(policy.Id);

                if (currentCount >= policy.MaxClients.Value)
                {
                    throw new ValidationException("Policy registration limit reached. Cannot approve more clients.");
                }
            }
        }

        await _clientProvider.UpdateStatusAsync(id, status);
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

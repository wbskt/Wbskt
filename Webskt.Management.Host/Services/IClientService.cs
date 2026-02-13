using Webskt.Common.Abstraction.Models.Management;

namespace Webskt.Management.Host.Services;

public interface IClientService
{
    Task<IReadOnlyCollection<ClientResponse>> GetAllAsync();
    Task<IReadOnlyCollection<ClientResponse>> GetByPolicyIdAsync(int policyId);
    Task UpdateStatusAsync(int id, ClientStatus status);
}

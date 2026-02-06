using Webskt.Management.Host.Models;

namespace Webskt.Management.Host.Services;

public interface IClientService
{
    Task<IReadOnlyCollection<ClientResponse>> GetAllAsync();
    Task<IReadOnlyCollection<ClientResponse>> GetByPolicyRefIdAsync(Guid policyRefId);
}

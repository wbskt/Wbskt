using Webskt.Common.Abstraction.Models.Management;
using Webskt.Management.Host.Models;
using Webskt.Common.Abstraction.Interfaces;

namespace Webskt.Management.Host.Providers;

public interface IClientProvider : IReferenceProvider
{
    Task<int> GetRegisteredCountByPolicyIdAsync(int policyId);
    Task<IReadOnlyCollection<Client>> GetAllAsync();
    Task<IReadOnlyCollection<Client>> GetByPolicyIdAsync(int policyId);
    Task<Client> InsertClientAsync(int policyId, string name, string secret, ClientStatus status);
    Task<Client> VerifyAsync(Guid refId, string secret);
}

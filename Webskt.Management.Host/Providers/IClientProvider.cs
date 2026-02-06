using Webskt.Management.Host.Models;

namespace Webskt.Management.Host.Providers;

public interface IClientProvider
{
    Task<RegistrationPolicy> GetPolicyByPinAsync(string pin);
    Task<int> GetRegisteredCountByPolicyIdAsync(int policyId);
    Task<Client> InsertClientAsync(int policyId, string name, string secret, ClientStatus status);
}

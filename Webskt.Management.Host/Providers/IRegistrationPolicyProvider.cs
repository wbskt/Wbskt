using Webskt.Management.Host.Models;

namespace Webskt.Management.Host.Providers;

public interface IRegistrationPolicyProvider
{
    Task<int> FindByRefIdAsync(Guid refId);
    Task<RegistrationPolicy> GetByRefIdAsync(Guid refId);
    Task<RegistrationPolicy> GetByPinAsync(string pin);
    Task<IReadOnlyCollection<RegistrationPolicy>> GetAllAsync();
    Task<RegistrationPolicy> InsertAsync(RegistrationPolicyRequest request);
}

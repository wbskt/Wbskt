using Webskt.Common.Abstraction.Models.Management;
using Webskt.Management.Host.Models;
using Webskt.Common.Abstraction.Interfaces;

namespace Webskt.Management.Host.Providers;

public interface IRegistrationPolicyProvider : IReferenceProvider
{
    Task<RegistrationPolicy> GetByRefIdAsync(Guid refId);
    Task<RegistrationPolicy> GetByPinAsync(string pin);
    Task<IReadOnlyCollection<RegistrationPolicy>> GetAllAsync();
    Task<RegistrationPolicy> InsertAsync(RegistrationPolicyRequest request);
}

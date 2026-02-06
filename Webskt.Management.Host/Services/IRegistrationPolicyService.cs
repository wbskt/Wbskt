using Webskt.Management.Host.Models;

namespace Webskt.Management.Host.Services;

public interface IRegistrationPolicyService
{
    Task<IReadOnlyCollection<RegistrationPolicyResponse>> GetAllAsync();
    Task<RegistrationPolicyResponse> GetByRefIdAsync(Guid refId);
    Task<RegistrationPolicyResponse> CreateAsync(RegistrationPolicyRequest request);
}

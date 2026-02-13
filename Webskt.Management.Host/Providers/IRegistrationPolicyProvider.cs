using Webskt.Common.Abstraction.Models.Management;
using Webskt.Management.Host.Models;
using Webskt.Common.Abstraction.Interfaces;
using Webskt.Common.Abstraction.Models;

namespace Webskt.Management.Host.Providers;

public interface IRegistrationPolicyProvider : IReferenceProvider
{
    Task<RegistrationPolicy> GetByRefIdAsync(Guid refId, CancellationToken cancellationToken = default);
    Task<RegistrationPolicy> GetByPinAsync(string pin, CancellationToken cancellationToken = default);
    Task<IPagedList<RegistrationPolicy>> GetAllAsync(bool? autoApproval, string? name, int skip, int take, CancellationToken cancellationToken = default);
    Task<RegistrationPolicy> InsertAsync(RegistrationPolicyRequest request, CancellationToken cancellationToken = default);
}

using Wbskt.Common.Abstraction.Models;
using Wbskt.Common.Abstraction.Models.Management;
using Wbskt.Foundation.Abstraction;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Providers;

public interface IRegistrationPolicyProvider : IReferenceProvider
{
    Task<RegistrationPolicy> GetByRefIdAsync(Guid refId, CancellationToken cancellationToken = default);
    Task<RegistrationPolicy> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<RegistrationPolicy> GetByPinAsync(string pin, CancellationToken cancellationToken = default);
    Task<IPagedList<RegistrationPolicy>> GetAllAsync(bool? autoApproval, string? name, int skip, int take, CancellationToken cancellationToken = default);
    Task<RegistrationPolicy> InsertAsync(int workspaceId, RegistrationPolicyRequest request, CancellationToken cancellationToken = default);
}

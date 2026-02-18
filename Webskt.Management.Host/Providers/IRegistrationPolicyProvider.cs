using Webskt.Common.Abstraction.Models.Management;
using Webskt.Management.Host.Models;
using Webskt.Common.Abstraction.Interfaces;
using Webskt.Common.Abstraction.Models;

namespace Webskt.Management.Host.Providers;

public interface IRegistrationPolicyProvider : IReferenceProvider
{
    Task<RegistrationPolicy> GetByRefIdAsync(Guid refId, CancellationToken cancellationToken = default);
    Task<RegistrationPolicy> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<RegistrationPolicy> GetByPinAsync(string pin, CancellationToken cancellationToken = default);
    Task<IPagedList<RegistrationPolicy>> GetAllAsync(bool? autoApproval, string? name, int skip, int take, CancellationToken cancellationToken = default);
    Task<RegistrationPolicy> InsertAsync(int workspaceId, RegistrationPolicyRequest request, CancellationToken cancellationToken = default);
    Task<int> FindIdByRefIdAsync(Guid referenceId, CancellationToken cancellationToken = default);
}

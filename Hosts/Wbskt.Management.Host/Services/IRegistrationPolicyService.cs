using Wbskt.Management.Host.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

public interface IRegistrationPolicyService
{
    Task<IPagedList<RegistrationPolicyResponse>> GetAllAsync(int workSpaceId, bool? autoApproval, string? name,
        int skip, int take, CancellationToken cancellationToken = default);
    Task<RegistrationPolicyResponse> GetByRefIdAsync(Guid refId, CancellationToken cancellationToken = default);
    Task<RegistrationPolicy> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<RegistrationPolicyResponse> CreateAsync(int workspaceId, RegistrationPolicyRequest request, CancellationToken cancellationToken = default);
    Task UpdateAsync(int workspaceId, int policyId, UpdateRegistrationPolicyRequest request, CancellationToken cancellationToken = default);
    Task DisableAsync(int workspaceId, int policyId, CancellationToken cancellationToken = default);
}

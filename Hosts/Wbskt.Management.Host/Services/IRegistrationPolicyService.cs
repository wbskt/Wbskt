using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

public interface IRegistrationPolicyService
{
    Task<Result<IPagedList<RegistrationPolicyResponse>>> GetAllAsync(int workSpaceId, bool? autoApproval, string? name,
        int skip, int take, CancellationToken cancellationToken = default);
    Task<Result<RegistrationPolicyResponse>> GetByRefIdAsync(Guid refId, CancellationToken cancellationToken = default);
    Task<Result<RegistrationPolicy>> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Result<RegistrationPolicyResponse>> CreateAsync(int workspaceId, RegistrationPolicyRequest request, CancellationToken cancellationToken = default);
    Task<Result> UpdateAsync(int workspaceId, int policyId, UpdateRegistrationPolicyRequest request, CancellationToken cancellationToken = default);
    Task<Result> DisableAsync(int workspaceId, int policyId, CancellationToken cancellationToken = default);
}

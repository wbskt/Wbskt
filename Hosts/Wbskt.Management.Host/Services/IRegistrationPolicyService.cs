using Wbskt.Infrastructure;
using Wbskt.Management.Host.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services;

public interface IRegistrationPolicyService
{
    Task<Result<IPagedList<RegistrationPolicyResponse>>> GetAllAsync(int workSpaceId, bool? autoApproval, string? name,
        int skip, int take, CancellationToken cancellationToken = default);
    /// <summary>
    /// The policy <paramref name="refId"/> names, if <paramref name="workspaceId"/> owns it.
    /// Otherwise <c>POLICY_NOT_FOUND</c>, the same for another workspace's policy as for none.
    /// </summary>
    Task<Result<RegistrationPolicy>> FindInWorkspaceAsync(int workspaceId, Guid refId, CancellationToken cancellationToken = default);
    /// <summary><see cref="FindInWorkspaceAsync"/> as the response the API returns.</summary>
    Task<Result<RegistrationPolicyResponse>> GetAsync(int workspaceId, Guid policyRefId, CancellationToken cancellationToken = default);
    Task<Result<RegistrationPolicyResponse>> CreateAsync(int workspaceId, RegistrationPolicyRequest request, CancellationToken cancellationToken = default);
    Task<Result> UpdateAsync(int workspaceId, Guid policyRefId, UpdateRegistrationPolicyRequest request, CancellationToken cancellationToken = default);
    /// <summary>Replaces the policy's PIN. Devices already registered are unaffected.</summary>
    Task<Result<RegistrationPolicyResponse>> RotatePinAsync(int workspaceId, Guid policyRefId, CancellationToken cancellationToken = default);
    Task<Result> DisableAsync(int workspaceId, Guid policyRefId, CancellationToken cancellationToken = default);
}

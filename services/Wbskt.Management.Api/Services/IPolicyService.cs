using Wbskt.Management.Api.Contracts;

namespace Wbskt.Management.Api.Services;

public interface IPolicyService
{
    Task<PolicyResponse> CreatePolicyAsync(CreatePolicyRequest request, CancellationToken cancellationToken);
    Task<List<PolicyResponse>> GetPoliciesAsync(CancellationToken cancellationToken);
    Task<PolicyResponse?> GetPolicyAsync(Guid refId, CancellationToken cancellationToken);
    Task<PolicyResponse> UpdatePolicyAsync(Guid refId, UpdatePolicyRequest request, CancellationToken cancellationToken);
    Task DeletePolicyAsync(Guid refId, CancellationToken cancellationToken);
}

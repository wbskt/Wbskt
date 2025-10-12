using Wbskt.Core.Service.Contracts;

namespace Wbskt.Core.Service.Services;

public interface IPolicyService
{
    Task<PolicyResponse> CreatePolicyAsync(int userId, CreatePolicyRequest request, CancellationToken cancellationToken);
    Task<List<PolicyResponse>> GetPoliciesAsync(int userId, CancellationToken cancellationToken);
    Task<PolicyResponse?> GetPolicyAsync(int userId, int policyId, CancellationToken cancellationToken);
    Task<PolicyResponse> UpdatePolicyAsync(int userId, int policyId, UpdatePolicyRequest request, CancellationToken cancellationToken);
    Task DeletePolicyAsync(int userId, int policyId, CancellationToken cancellationToken);
}

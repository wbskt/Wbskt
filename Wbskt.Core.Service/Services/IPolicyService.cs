using Wbskt.Core.Service.Contracts;

namespace Wbskt.Core.Service.Services;

public interface IPolicyService
{
    Task<PolicyResponse> CreatePolicyAsync(int userId, CreatePolicyRequest request, CancellationToken cancellationToken);
    Task<List<PolicyResponse>> GetPoliciesAsync(int userId, CancellationToken cancellationToken);
    Task<PolicyResponse?> GetPolicyAsync(int userId, Guid refId, CancellationToken cancellationToken);
    Task<PolicyResponse> UpdatePolicyAsync(int userId, Guid refId, UpdatePolicyRequest request, CancellationToken cancellationToken);
    Task DeletePolicyAsync(int userId, Guid refId, CancellationToken cancellationToken);
}

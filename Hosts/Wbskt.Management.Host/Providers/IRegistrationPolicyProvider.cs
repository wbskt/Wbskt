using Wbskt.Management.Host.Models;
using Wbskt.Models;
using Wbskt.Primitives;

namespace Wbskt.Management.Host.Providers;

public interface IRegistrationPolicyProvider : IReferenceProvider
{
    Task<RegistrationPolicy> GetByRefIdAsync(Guid refId, CancellationToken cancellationToken = default);
    Task<RegistrationPolicy> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<RegistrationPolicy> GetByPinAsync(string pin, CancellationToken cancellationToken = default);
    Task<IPagedList<RegistrationPolicy>> GetAllAsync(int workSpaceId, bool? autoApproval, string? name, int skip,
        int take, CancellationToken cancellationToken = default);
    Task<RegistrationPolicy> InsertAsync(int workspaceId, RegistrationPolicyRequest request, CancellationToken cancellationToken = default);
    Task UpdateAsync(int workspaceId, int id, UpdateRegistrationPolicyRequest request, CancellationToken cancellationToken = default);
    /// <summary>Draws a new PIN for the policy, retrying on a clash, and returns the updated policy.</summary>
    Task<RegistrationPolicy> RotatePinAsync(int workspaceId, int id, CancellationToken cancellationToken = default);
    Task DisableAsync(int workspaceId, int id, CancellationToken cancellationToken = default);
}

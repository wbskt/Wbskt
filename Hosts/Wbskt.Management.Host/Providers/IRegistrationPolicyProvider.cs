using Wbskt.Management.Host.Models;
using Wbskt.Management.Models;
using Wbskt.Models;
using Wbskt.Primitives;

namespace Wbskt.Management.Host.Providers;

public interface IRegistrationPolicyProvider : IReferenceProvider
{
    Task<RegistrationPolicy?> FindByRefIdAsync(Guid refId, CancellationToken cancellationToken = default);
    Task<RegistrationPolicy?> FindByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<RegistrationPolicy?> FindByPinAsync(string pin, CancellationToken cancellationToken = default);
    Task<IPagedList<RegistrationPolicy>> GetAllAsync(int workSpaceId, bool? autoApproval, string? name, int skip,
        int take, CancellationToken cancellationToken = default);
    Task<RegistrationPolicy> InsertAsync(int workspaceId, RegistrationPolicyRequest request, CancellationToken cancellationToken = default);
    Task UpdateAsync(int workspaceId, int id, UpdateRegistrationPolicyRequest request, CancellationToken cancellationToken = default);
    /// <summary>Draws a new PIN for the policy, retrying on a clash, and returns the updated policy.</summary>
    Task<RegistrationPolicy> RotatePinAsync(int workspaceId, int id, CancellationToken cancellationToken = default);
    Task DisableAsync(int workspaceId, int id, CancellationToken cancellationToken = default);

    /// <summary>Deletes a policy with no clients, unpinning its templates; see <c>dbo.RegistrationPolicy_Delete</c>.</summary>
    Task<PolicyDeleteOutcome> DeleteAsync(int workspaceId, int id, CancellationToken cancellationToken = default);
}

/// <param name="ClientCount">The clients still enrolled through the policy, which kept it from being deleted.</param>
public sealed record PolicyDeleteOutcome(bool Deleted, int ClientCount);

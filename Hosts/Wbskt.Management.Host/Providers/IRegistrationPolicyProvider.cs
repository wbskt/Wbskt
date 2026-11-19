using Wbskt.Management.Host.Models;
using Wbskt.Management.Models;
using Wbskt.Models;
using Wbskt.Primitives;

namespace Wbskt.Management.Host.Providers;

public interface IRegistrationPolicyProvider
{
    Task<RegistrationPolicy?> FindByRefIdAsync(Guid refId, CancellationToken cancellationToken = default);
    Task<RegistrationPolicy?> FindByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<RegistrationPolicy?> FindByPinAsync(string pin, CancellationToken cancellationToken = default);
    Task<IPagedList<RegistrationPolicy>> GetAllAsync(int workSpaceId, bool? autoApproval, string? name, int skip,
        int take, CancellationToken cancellationToken = default);
    Task<RegistrationPolicy> InsertAsync(int workspaceId, RegistrationPolicyRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the edit. Returns null when it went through, or, when <c>maxClients</c> is below the clients
    /// already registered, that count with nothing changed (checked under the policy's lock).
    /// </summary>
    Task<int?> UpdateAsync(int workspaceId, int id, UpdateRegistrationPolicyRequest request, CancellationToken cancellationToken = default);
    /// <summary>Draws a new PIN for the policy, retrying on a clash, and returns the updated policy.</summary>
    Task<RegistrationPolicy> RotatePinAsync(int workspaceId, int id, CancellationToken cancellationToken = default);

    /// <summary>Deletes a policy with no clients, unpinning its templates; see <c>dbo.RegistrationPolicy_Delete</c>.</summary>
    Task<PolicyDeleteOutcome> DeleteAsync(int workspaceId, int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a registration refused at <paramref name="kind"/>'s limit through the policy is to be
    /// announced: true, and recorded, when none was within <paramref name="interval"/> before
    /// <paramref name="nowUtc"/>; see <c>dbo.RegistrationLimitNotice_Claim</c>.
    /// </summary>
    Task<bool> TryClaimLimitNoticeAsync(int policyId, RegistrationLimitKind kind, DateTime nowUtc, TimeSpan interval, CancellationToken cancellationToken = default);
}

/// <param name="ClientCount">The clients still enrolled through the policy, which kept it from being deleted.</param>
public sealed record PolicyDeleteOutcome(bool Deleted, int ClientCount);

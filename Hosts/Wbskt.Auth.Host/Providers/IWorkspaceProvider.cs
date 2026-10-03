using Wbskt.Auth.Host.Models;
using Wbskt.Models;
using Wbskt.Primitives;

namespace Wbskt.Auth.Host.Providers;

internal interface IWorkspaceProvider : IReferenceProvider
{
    Task<Guid> CreateWorkspaceAsync(string name, string description, int ownerId, int tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Workspace>> GetWorkspacesForUserAsync(int userId, CancellationToken cancellationToken = default);
    Task AddUserToWorkspaceAsync(int workspaceId, int userId, CancellationToken cancellationToken = default);
    Task RemoveUserFromWorkspaceAsync(int workspaceId, int userId, CancellationToken cancellationToken = default);
    Task<IPagedList<TenantMemberResponse>> GetWorkspaceMembersAsync(int workspaceId, int skip, int take, CancellationToken cancellationToken = default);
    Task<bool> VerifyWorkspaceMembershipAsync(int userId, int workspaceId, CancellationToken cancellationToken = default);
    Task UpdateWorkspaceAsync(int workspaceId, string name, string? description, CancellationToken cancellationToken = default);
    Task DeleteWorkspaceAsync(int workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes <paramref name="userId"/> the owner, adding them as a member if needed. Throws 50009 when
    /// they are not in the workspace's tenant.
    /// </summary>
    Task SetOwnerAsync(int workspaceId, int userId, CancellationToken cancellationToken = default);
}

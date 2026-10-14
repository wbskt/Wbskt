using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Models;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Workspaces and their members. The operations on one workspace arrive authorized: the auth host's
/// workspace permission filter has checked the caller's membership and permission before the
/// action runs.
/// </summary>
public interface IWorkspaceService
{
    Task<Result<WorkspaceResponse>> CreateWorkspaceAsync(int ownerId, CreateWorkspaceRequest request, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<WorkspaceResponse>>> GetWorkspacesForUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<Result> AddUserToWorkspaceAsync(int workspaceId, AddMemberRequest request, CancellationToken cancellationToken = default);
    Task<Result> RemoveUserFromWorkspaceAsync(int workspaceId, Guid userRef, CancellationToken cancellationToken = default);
    Task<Result<IPagedList<TenantMemberResponse>>> GetMembersAsync(int workspaceId, int skip, int take, CancellationToken cancellationToken = default);
    Task<Result> UpdateWorkspaceAsync(int workspaceId, CreateWorkspaceRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteWorkspaceAsync(int callerId, int workspaceId, CancellationToken cancellationToken = default);
    Task<Result> TransferOwnershipAsync(int callerId, int workspaceId, Guid newOwnerRef, CancellationToken cancellationToken = default);

    /// <summary>
    /// Membership gate plus effective permission set for <paramref name="userId"/> in the workspace.
    /// The user is an explicit argument rather than being read from the ambient HTTP context, so the
    /// method is callable and testable outside a request.
    /// </summary>
    Task<Result<IReadOnlyCollection<string>>> ResolveAccessAsync(int userId, int workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The same gate and permission set, starting from the workspace's public reference, in a single
    /// database call. Fails Forbidden with <c>WORKSPACE_NOT_FOUND</c> when the reference matches no
    /// workspace and <c>WORKSPACE_UNAUTHORIZED</c> when the user is not a member.
    /// </summary>
    Task<Result<WorkspaceAccessResolution>> ResolveAccessAsync(int userId, Guid workspaceRef, CancellationToken cancellationToken = default);

    /// <summary>
    /// The ids of the workspaces deleted in [<paramref name="sinceUtc"/>, <paramref name="untilUtc"/>),
    /// for the management host's deleted workspace sweep. Not a user's question: the internal route
    /// that asks it is authorized by the shared internal key. Fails Validation when the window is
    /// empty or reversed.
    /// </summary>
    Task<Result<IReadOnlyCollection<int>>> GetDeletedBetweenAsync(DateTime sinceUtc, DateTime untilUtc, CancellationToken cancellationToken = default);
}

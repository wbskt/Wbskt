using Wbskt.Auth.Host.Services;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Auth.Host.Authorization;

/// <summary>
/// Resolves the auth host's own workspace routes with the same resolve the management host calls over
/// HTTP (<see cref="IWorkspaceService.ResolveAccessAsync(int, Guid, CancellationToken)"/>), so both
/// hosts answer an unknown or foreign workspace identically.
/// </summary>
public sealed class WorkspaceServiceAccessResolver(IIdentityService identity, IWorkspaceService workspaces) : IWorkspaceAccessResolver
{
    public async Task<Result<PermissionScope>> ResolveAsync(Guid workspaceRef, CancellationToken cancellationToken)
    {
        if (!identity.TryGetUserIdentity(out var caller))
        {
            return Result<PermissionScope>.Failure(Error.Unauthorized("AUTH_UNAUTHORIZED", "Unauthorized access."));
        }

        var access = await workspaces.ResolveAccessAsync(caller.UserId, workspaceRef, cancellationToken);
        return access.IsFailure
            ? Result<PermissionScope>.Failure(access.Error)
            : Result<PermissionScope>.Success(PermissionScope.Of(access.Value.WorkspaceId, access.Value.Permissions));
    }
}

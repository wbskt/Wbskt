using Wbskt.Auth.Host.Services;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Auth.Host.Authorization;

/// <summary>
/// Enforces <see cref="RequiresPermissionAttribute"/> on the auth host's own workspace routes, with
/// the same resolve the management host calls over HTTP (<see cref="IWorkspaceService.ResolveAccessAsync(int, Guid, CancellationToken)"/>),
/// so both hosts answer an unknown or foreign workspace identically.
/// </summary>
public sealed class WorkspacePermissionFilter : ScopedPermissionFilter<RequiresPermissionAttribute>
{
    public const string WorkspaceRefRouteKey = "workspaceRef";

    protected override string RouteKey => WorkspaceRefRouteKey;

    protected override async Task<Result<PermissionScope>> ResolveAsync(HttpContext httpContext, Guid scopeRef, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        var identity = httpContext.RequestServices.GetRequiredService<IIdentityService>();
        if (!identity.TryGetUserIdentity(out var caller))
        {
            return Result<PermissionScope>.Failure(Error.Unauthorized("AUTH_UNAUTHORIZED", "Unauthorized access."));
        }

        var workspaces = httpContext.RequestServices.GetRequiredService<IWorkspaceService>();
        var access = await workspaces.ResolveAccessAsync(caller.UserId, scopeRef, cancellationToken);
        return access.IsFailure
            ? Result<PermissionScope>.Failure(access.Error)
            : Result<PermissionScope>.Success(new PermissionScope(access.Value.WorkspaceId, access.Value.Permissions.ToHashSet(StringComparer.Ordinal)));
    }
}

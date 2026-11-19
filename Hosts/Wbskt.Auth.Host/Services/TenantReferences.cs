using Wbskt.Auth.Host.Providers;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Services;

/// <summary>A tenant and the internal id of something named inside it.</summary>
internal readonly record struct ResolvedEntity(int TenantId, int EntityId);

/// <summary>
/// The tenant itself is resolved, and the caller's permission in it checked, by
/// <c>TenantPermissionFilter</c> before the action runs; these find the entity named inside it.
/// </summary>
/// <remarks>
/// A reference inside the tenant that names nothing, or names something in another tenant, is a 404
/// with the same code and message either way. A 403 there would only ever mean a missing permission.
/// </remarks>
internal static class TenantReferences
{
    public static readonly Error UserNotFound = Error.NotFound("USER_NOT_FOUND", "User not found in this tenant.");
    public static readonly Error RoleNotFound = Error.NotFound("ROLE_NOT_FOUND", "Role not found in this tenant.");
    public static readonly Error GroupNotFound = Error.NotFound("GROUP_NOT_FOUND", "Group not found in this tenant.");
    public static readonly Error WorkspaceNotFound = Error.NotFound("WORKSPACE_NOT_FOUND", "Workspace not found in this tenant.");

    /// <summary>
    /// A member of the tenant. Scoped to tenant membership: an administrator cannot reach a user
    /// outside their own tenant and pull them into its permission graph.
    /// </summary>
    public static async Task<Result<ResolvedEntity>> ResolveMemberAsync(ITenantProvider tenants, int tenantId, Guid userRef, CancellationToken cancellationToken)
    {
        if (await tenants.FindUserIdByRefIdInTenantAsync(userRef, tenantId, cancellationToken) is not { } userId)
        {
            return Result<ResolvedEntity>.Failure(UserNotFound);
        }

        return Result<ResolvedEntity>.Success(new ResolvedEntity(tenantId, userId));
    }

    /// <summary>A workspace an assignment or invitation names, looked up within the tenant.</summary>
    public static async Task<Result<int>> ResolveWorkspaceAsync(IWorkspaceProvider workspaces, int tenantId, Guid workspaceRef, CancellationToken cancellationToken)
    {
        var workspaceId = await workspaces.FindIdByRefIdInTenantAsync(workspaceRef, tenantId, cancellationToken);
        return workspaceId is { } id ? Result<int>.Success(id) : Result<int>.Failure(WorkspaceNotFound);
    }
}

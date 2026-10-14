using Wbskt.Auth.Host.Providers;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Services;

/// <summary>A tenant and the internal id of something named inside it.</summary>
internal readonly record struct ResolvedEntity(int TenantId, int EntityId);

/// <summary>
/// The tenant itself is resolved, and the caller's permission in it checked, by
/// <c>TenantPermissionFilter</c> before the action runs; these find the entity named inside it.
/// </summary>
internal static class TenantReferences
{
    /// <summary>
    /// A member of the tenant. Scoped to tenant membership: an administrator cannot reach a user
    /// outside their own tenant and pull them into its permission graph.
    /// </summary>
    public static async Task<Result<ResolvedEntity>> ResolveMemberAsync(ITenantProvider tenants, int tenantId, Guid userRef, CancellationToken cancellationToken)
    {
        var userId = await tenants.FindUserIdByRefIdInTenantAsync(userRef, tenantId, cancellationToken);
        if (userId <= 0)
        {
            return Result<ResolvedEntity>.Failure(Error.Forbidden("USER_NOT_FOUND", "User not found in this tenant."));
        }

        return Result<ResolvedEntity>.Success(new ResolvedEntity(tenantId, userId));
    }
}

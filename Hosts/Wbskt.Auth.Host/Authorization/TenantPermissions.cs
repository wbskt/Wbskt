using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Wbskt.Auth.Host.Providers;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Authorization;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Auth.Host.Authorization;

/// <summary>
/// A tenant administration action: the caller must belong to the tenant named by the
/// <c>tenantRef</c> route value and hold these permissions tenant-wide in it. A workspace-scoped
/// grant deliberately does not satisfy it. The action receives the tenant's internal id through
/// <see cref="FromTenantAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequiresTenantPermissionAttribute(params string[] permissions) : PermissionRequirementAttribute(permissions);

/// <summary>Binds the internal id of the tenant <see cref="TenantPermissionFilter"/> resolved for this request.</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FromTenantAttribute : ModelBinderAttribute
{
    public FromTenantAttribute() : base(typeof(ScopeIdModelBinder<RequiresTenantPermissionAttribute>))
    {
        BindingSource = BindingSource.Special;
    }
}

/// <summary>
/// Enforces <see cref="RequiresTenantPermissionAttribute"/>. A tenant the caller does not belong to
/// does not resolve, so it reads exactly like one that does not exist (403 <c>TENANT_NOT_FOUND</c>).
/// </summary>
public sealed class TenantPermissionFilter : ScopedPermissionFilter<RequiresTenantPermissionAttribute>
{
    public const string TenantRefRouteKey = "tenantRef";

    protected override string RouteKey => TenantRefRouteKey;

    protected override async Task<Result<PermissionScope>> ResolveAsync(HttpContext httpContext, Guid scopeRef, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        var identity = httpContext.RequestServices.GetRequiredService<IIdentityService>();
        if (!identity.TryGetUserIdentity(out var caller))
        {
            return Result<PermissionScope>.Failure(Error.Unauthorized("AUTH_UNAUTHORIZED", "Unauthorized access."));
        }

        var provider = httpContext.RequestServices.GetRequiredService<IAuthProvider>();
        int tenantId = await provider.FindTenantIdByRefIdForUserAsync(scopeRef, caller.UserId, cancellationToken);
        if (tenantId <= 0)
        {
            return Result<PermissionScope>.Failure(Error.Forbidden("TENANT_NOT_FOUND", "Tenant not found."));
        }

        var granted = new HashSet<string>(StringComparer.Ordinal);
        foreach (string permission in permissions)
        {
            // WorkspaceId null: tenant administration needs the permission tenant-wide.
            if (await provider.VerifyPermissionAsync(caller.UserId, tenantId, null, permission, cancellationToken))
            {
                granted.Add(permission);
            }
        }

        return Result<PermissionScope>.Success(new PermissionScope(tenantId, granted));
    }
}

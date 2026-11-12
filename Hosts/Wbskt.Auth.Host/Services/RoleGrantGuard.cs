using Wbskt.Auth.Host.Providers;
using Wbskt.Infrastructure;
using Wbskt.Primitives.Constants;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Granting or taking away a role needs roles.manage tenant-wide. An invitation that names a role, or
/// adding a member to, removing one from or deleting a group that carries roles, does it as surely as
/// assigning or removing the role, so those paths ask here on top of the users.manage their endpoints
/// declare. Removing or suspending a tenant member does not: it ends their access as a whole, which is
/// what users.manage is for.
/// </summary>
internal static class RoleGrantGuard
{
    public static readonly Error RolesManageRequired =
        Error.Forbidden("PERMISSION_UNAUTHORIZED", $"user does not have permission(s) {PermissionNames.RolesManage}");

    public static Task<bool> MayGrantRolesAsync(IAccessControlProvider accessControl, int callerId, int tenantId, CancellationToken cancellationToken) =>
        accessControl.VerifyPermissionAsync(callerId, tenantId, null, PermissionNames.RolesManage, cancellationToken);
}

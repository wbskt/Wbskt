using Wbskt.Auth.Host.Providers;
using Wbskt.Infrastructure;
using Wbskt.Primitives.Constants;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Granting a role needs roles.manage tenant-wide, however the grant is made. An invitation that names
/// a role, or adding a member to a group that carries roles, grants it as surely as assigning it does,
/// so those paths ask here on top of the users.manage their endpoints declare.
/// </summary>
internal static class RoleGrantGuard
{
    public static readonly Error RolesManageRequired =
        Error.Forbidden("PERMISSION_UNAUTHORIZED", $"user does not have permission(s) {PermissionNames.RolesManage}");

    public static Task<bool> MayGrantRolesAsync(IAccessControlProvider accessControl, int callerId, int tenantId, CancellationToken cancellationToken) =>
        accessControl.VerifyPermissionAsync(callerId, tenantId, null, PermissionNames.RolesManage, cancellationToken);
}

using Wbskt.Auth.Models;
using Wbskt.Models;

namespace Wbskt.Auth.Host.Providers;

/// <summary>
/// Access control: a tenant's roles and groups, the permission catalogue, the assignments between
/// them, and the checks that read the result.
/// </summary>
internal interface IAccessControlProvider
{
    // Reference resolution, scoped to the tenant rather than being a bare Guid lookup, so a role or
    // group belonging to another tenant does not resolve and cannot be acted on or probed for. This
    // is why these are not IReferenceProvider.FindIdByRefIdAsync, which takes only a Guid.
    Task<int> FindRoleIdByRefIdAsync(Guid roleRef, int tenantId, CancellationToken cancellationToken = default);
    Task<int> FindGroupIdByRefIdAsync(Guid groupRef, int tenantId, CancellationToken cancellationToken = default);

    Task<bool> VerifyPermissionAsync(int userId, int tenantId, int? workspaceId, string permissionSlug, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<string>> GetEffectivePermissionsAsync(int userId, int workspaceId, CancellationToken cancellationToken = default);

    Task<IPagedList<PermissionResponse>> GetPermissionsAsync(int skip, int take, CancellationToken cancellationToken = default);
    Task<IPagedList<RoleResponse>> GetRolesAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default);
    Task<IPagedList<GroupResponse>> GetGroupsAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default);

    Task<Guid> InsertRoleAsync(string name, string? description, int tenantId, CancellationToken cancellationToken = default);
    Task UpdateRoleAsync(int roleId, int tenantId, string name, string? description, CancellationToken cancellationToken = default);
    Task DeleteRoleAsync(int roleId, int tenantId, CancellationToken cancellationToken = default);

    Task<Guid> InsertGroupAsync(string name, int? parentGroupId, int tenantId, CancellationToken cancellationToken = default);
    Task UpdateGroupAsync(int groupId, int tenantId, string name, CancellationToken cancellationToken = default);
    Task DeleteGroupAsync(int groupId, int tenantId, CancellationToken cancellationToken = default);

    Task InsertUserGroupAsync(int userId, int groupId, int tenantId, CancellationToken cancellationToken = default);
    Task RemoveUserGroupAsync(int userId, int groupId, int tenantId, CancellationToken cancellationToken = default);

    Task GrantRolePermissionAsync(int roleId, string permissionSlug, bool isDeny, int tenantId, CancellationToken cancellationToken = default);
    Task RemoveRolePermissionAsync(int roleId, string permissionSlug, int tenantId, CancellationToken cancellationToken = default);
    Task GrantUserPermissionAsync(int userId, string permissionSlug, bool isDeny, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);
    Task RemoveUserPermissionAsync(int userId, string permissionSlug, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);

    Task AssignUserRoleAsync(int userId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);
    Task RemoveUserRoleAsync(int userId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);
    Task AssignGroupRoleAsync(int groupId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);
    Task RemoveGroupRoleAsync(int groupId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);

    // Assignment read-back. Without these an administrator can write the permission graph but never
    // inspect it, which makes an unexpected allow or deny impossible to diagnose through the API.
    Task<IReadOnlyCollection<RoleAssignmentResponse>> GetUserRolesAsync(int userId, int tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<UserPermissionAssignmentResponse>> GetUserPermissionsAsync(int userId, int tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<GroupMembershipResponse>> GetUserGroupsAsync(int userId, int tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RoleAssignmentResponse>> GetGroupRolesAsync(int groupId, int tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RolePermissionAssignmentResponse>> GetRolePermissionsAsync(int roleId, CancellationToken cancellationToken = default);
}

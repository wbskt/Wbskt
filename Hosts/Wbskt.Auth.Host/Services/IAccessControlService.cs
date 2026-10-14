using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Models;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Access control inside a tenant: roles, groups, the permission catalogue, and the assignments that
/// connect them to members.
/// <para>
/// The tenant arrives resolved and authorized by <c>TenantPermissionFilter</c>, as for
/// <see cref="ITenantService"/>; a method here only resolves the references named inside it.
/// </para>
/// </summary>
public interface IAccessControlService
{
    Task<Result<IPagedList<RoleResponse>>> GetRolesAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default);
    Task<Result<IPagedList<GroupResponse>>> GetGroupsAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default);
    Task<Result<IPagedList<PermissionResponse>>> GetPermissionsAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default);

    Task<Result<RoleResponse>> CreateRoleAsync(int tenantId, CreateRoleRequest request, CancellationToken cancellationToken = default);
    Task<Result> UpdateRoleAsync(int tenantId, Guid roleRef, UpdateRoleRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteRoleAsync(int tenantId, Guid roleRef, CancellationToken cancellationToken = default);

    Task<Result<GroupResponse>> CreateGroupAsync(int tenantId, CreateGroupRequest request, CancellationToken cancellationToken = default);
    Task<Result> UpdateGroupAsync(int tenantId, Guid groupRef, UpdateGroupRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteGroupAsync(int tenantId, Guid groupRef, CancellationToken cancellationToken = default);

    Task<Result> AddUserToGroupAsync(int tenantId, Guid userRef, Guid groupRef, CancellationToken cancellationToken = default);
    Task<Result> RemoveUserFromGroupAsync(int tenantId, Guid userRef, Guid groupRef, CancellationToken cancellationToken = default);

    Task<Result> GrantRolePermissionAsync(int tenantId, Guid roleRef, GrantRolePermissionRequest request, CancellationToken cancellationToken = default);
    Task<Result> RemoveRolePermissionAsync(int tenantId, Guid roleRef, string slug, CancellationToken cancellationToken = default);
    Task<Result> GrantUserPermissionAsync(int tenantId, Guid userRef, GrantPermissionRequest request, CancellationToken cancellationToken = default);
    Task<Result> RemoveUserPermissionAsync(int tenantId, Guid userRef, string slug, Guid? workspaceRef, CancellationToken cancellationToken = default);

    Task<Result> AssignUserRoleAsync(int tenantId, Guid userRef, Guid roleRef, Guid? workspaceRef, CancellationToken cancellationToken = default);
    Task<Result> RemoveUserRoleAsync(int tenantId, Guid userRef, Guid roleRef, Guid? workspaceRef, CancellationToken cancellationToken = default);
    Task<Result> AssignGroupRoleAsync(int tenantId, Guid groupRef, Guid roleRef, Guid? workspaceRef, CancellationToken cancellationToken = default);
    Task<Result> RemoveGroupRoleAsync(int tenantId, Guid groupRef, Guid roleRef, Guid? workspaceRef, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyCollection<RoleAssignmentResponse>>> GetUserRolesAsync(int tenantId, Guid userRef, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<UserPermissionAssignmentResponse>>> GetUserPermissionsAsync(int tenantId, Guid userRef, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<GroupMembershipResponse>>> GetUserGroupsAsync(int tenantId, Guid userRef, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<RoleAssignmentResponse>>> GetGroupRolesAsync(int tenantId, Guid groupRef, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<RolePermissionAssignmentResponse>>> GetRolePermissionsAsync(int tenantId, Guid roleRef, CancellationToken cancellationToken = default);
}

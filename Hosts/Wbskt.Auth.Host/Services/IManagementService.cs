using Wbskt.Auth.Host.Models;
using Wbskt.Infrastructure;
using Wbskt.Models;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Tenant administration: roles, groups, membership and permission assignments.
/// <para>
/// Every method takes the caller's ID plus public <c>Guid</c> references and performs its own
/// permission gate. Reference resolution lives here rather than in the controller (the usual home
/// for it, per Docs/Coding.Conventions.md) because these lookups are tenant-scoped: a role or user
/// reference can only be resolved once the tenant is known and the caller has been authorised
/// against it. Resolving in the controller would mean the controller orchestrating the
/// authorisation sequence, and would leak internal integer IDs into it.
/// </para>
/// </summary>
public interface IManagementService
{
    Task<Result<IReadOnlyCollection<TenantResponse>>> GetTenantsForUserAsync(int callerId, CancellationToken cancellationToken = default);

    Task<Result<IPagedList<RoleResponse>>> GetRolesAsync(int callerId, Guid tenantRef, int skip, int take, CancellationToken cancellationToken = default);
    Task<Result<IPagedList<GroupResponse>>> GetGroupsAsync(int callerId, Guid tenantRef, int skip, int take, CancellationToken cancellationToken = default);
    Task<Result<IPagedList<PermissionResponse>>> GetPermissionsAsync(int callerId, Guid tenantRef, int skip, int take, CancellationToken cancellationToken = default);
    Task<Result<IPagedList<TenantMemberResponse>>> GetMembersAsync(int callerId, Guid tenantRef, string? search, int skip, int take, CancellationToken cancellationToken = default);

    Task<Result<RoleResponse>> CreateRoleAsync(int callerId, Guid tenantRef, CreateRoleRequest request, CancellationToken cancellationToken = default);
    Task<Result> UpdateRoleAsync(int callerId, Guid tenantRef, Guid roleRef, UpdateRoleRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteRoleAsync(int callerId, Guid tenantRef, Guid roleRef, CancellationToken cancellationToken = default);

    Task<Result<GroupResponse>> CreateGroupAsync(int callerId, Guid tenantRef, CreateGroupRequest request, CancellationToken cancellationToken = default);
    Task<Result> UpdateGroupAsync(int callerId, Guid tenantRef, Guid groupRef, UpdateGroupRequest request, CancellationToken cancellationToken = default);
    Task<Result> DeleteGroupAsync(int callerId, Guid tenantRef, Guid groupRef, CancellationToken cancellationToken = default);

    Task<Result> AddUserToGroupAsync(int callerId, Guid tenantRef, Guid userRef, Guid groupRef, CancellationToken cancellationToken = default);
    Task<Result> RemoveUserFromGroupAsync(int callerId, Guid tenantRef, Guid userRef, Guid groupRef, CancellationToken cancellationToken = default);
    Task<Result> SetUserActiveAsync(int callerId, Guid tenantRef, Guid userRef, bool isActive, string ipAddress, CancellationToken cancellationToken = default);

    Task<Result> GrantRolePermissionAsync(int callerId, Guid tenantRef, Guid roleRef, GrantRolePermissionRequest request, CancellationToken cancellationToken = default);
    Task<Result> RemoveRolePermissionAsync(int callerId, Guid tenantRef, Guid roleRef, string slug, CancellationToken cancellationToken = default);
    Task<Result> GrantUserPermissionAsync(int callerId, Guid tenantRef, Guid userRef, GrantPermissionRequest request, CancellationToken cancellationToken = default);
    Task<Result> RemoveUserPermissionAsync(int callerId, Guid tenantRef, Guid userRef, string slug, Guid? workspaceRef, CancellationToken cancellationToken = default);

    Task<Result> AssignUserRoleAsync(int callerId, Guid tenantRef, Guid userRef, Guid roleRef, Guid? workspaceRef, CancellationToken cancellationToken = default);
    Task<Result> RemoveUserRoleAsync(int callerId, Guid tenantRef, Guid userRef, Guid roleRef, Guid? workspaceRef, CancellationToken cancellationToken = default);
    Task<Result> AssignGroupRoleAsync(int callerId, Guid tenantRef, Guid groupRef, Guid roleRef, Guid? workspaceRef, CancellationToken cancellationToken = default);
    Task<Result> RemoveGroupRoleAsync(int callerId, Guid tenantRef, Guid groupRef, Guid roleRef, Guid? workspaceRef, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyCollection<RoleAssignmentResponse>>> GetUserRolesAsync(int callerId, Guid tenantRef, Guid userRef, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<UserPermissionAssignmentResponse>>> GetUserPermissionsAsync(int callerId, Guid tenantRef, Guid userRef, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<GroupMembershipResponse>>> GetUserGroupsAsync(int callerId, Guid tenantRef, Guid userRef, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<RoleAssignmentResponse>>> GetGroupRolesAsync(int callerId, Guid tenantRef, Guid groupRef, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<RolePermissionAssignmentResponse>>> GetRolePermissionsAsync(int callerId, Guid tenantRef, Guid roleRef, CancellationToken cancellationToken = default);
}

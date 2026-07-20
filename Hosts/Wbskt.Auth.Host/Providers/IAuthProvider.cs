using Wbskt.Auth.Host.Models;
using Wbskt.Primitives;

namespace Wbskt.Auth.Host.Providers;

internal interface IAuthProvider : IReferenceProvider
{
    Task<User> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<User> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<int> InsertUserAsync(User user, CancellationToken cancellationToken = default);
    Task InsertRefreshTokenAsync(RefreshToken token, string ipAddress, CancellationToken cancellationToken = default);
    Task<RefreshToken> GetRefreshTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<bool> VerifyPermissionAsync(int userId, int tenantId, int? workspaceId, string permissionSlug, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<string>> GetEffectivePermissionsAsync(int userId, int workspaceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Tenant>> GetTenantsForUserAsync(int userId, CancellationToken cancellationToken = default);
    Task InsertTenantMemberAsync(int tenantId, int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<PermissionResponse>> GetPermissionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RoleResponse>> GetRolesAsync(int tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<GroupResponse>> GetGroupsAsync(int tenantId, CancellationToken cancellationToken = default);
    Task InsertRoleAsync(string name, string description, int tenantId, CancellationToken cancellationToken = default);
    Task InsertGroupAsync(string name, int? parentGroupId, int tenantId, CancellationToken cancellationToken = default);
    Task InsertUserGroupAsync(int userId, int groupId, int tenantId, CancellationToken cancellationToken = default);
    Task InsertPermissionAsync(string slug, string description, CancellationToken cancellationToken = default);
    Task GrantRolePermissionAsync(int roleId, string permissionSlug, bool isDeny, int tenantId, CancellationToken cancellationToken = default);
    Task GrantUserPermissionAsync(int userId, string permissionSlug, bool isDeny, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);
    Task AssignUserRoleAsync(int userId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);
    Task RemoveUserRoleAsync(int userId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);
    Task AssignGroupRoleAsync(int groupId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);
    Task RemoveGroupRoleAsync(int groupId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);
}
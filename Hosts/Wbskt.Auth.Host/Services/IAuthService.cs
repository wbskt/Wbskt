using Wbskt.Auth.Host.Models;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Services;

public interface IAuthService
{
    Task<Result<LoginResponse>> LoginAsync(string email, string password, string ipAddress, CancellationToken cancellationToken = default);
    Task<Result<LoginResponse>> RefreshTokenAsync(string token, string ipAddress, CancellationToken cancellationToken = default);
    Task<Result> LogoutAsync(string token, string ipAddress, CancellationToken cancellationToken = default);
    Task<Result> LogoutAllAsync(int userId, string ipAddress, CancellationToken cancellationToken = default);
    Task<Result> SetUserActiveAsync(int userId, bool isActive, string ipAddress, CancellationToken cancellationToken = default);
    Task<Result<bool>> VerifyPermissionAsync(int userId, int tenantId, int? workspaceId, string permissionSlug, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<TenantResponse>>> GetTenantsForUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<PermissionResponse>>> GetPermissionsAsync(CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<RoleResponse>>> GetRolesAsync(int tenantId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<GroupResponse>>> GetGroupsAsync(int tenantId, CancellationToken cancellationToken = default);
    Task<Result> RegisterUserAsync(string username, string email, string password, CancellationToken cancellationToken = default);
    Task<Result> CreateRoleAsync(string name, string description, int tenantId, CancellationToken cancellationToken = default);
    Task<Result> CreateGroupAsync(string name, int? parentGroupId, int tenantId, CancellationToken cancellationToken = default);
    Task<Result> AddUserToGroupAsync(int userId, int groupId, int tenantId, CancellationToken cancellationToken = default);
    Task<Result> GrantRolePermissionAsync(int roleId, string permissionSlug, bool isDeny, int tenantId, CancellationToken cancellationToken = default);
    Task<Result> GrantUserPermissionAsync(int userId, string permissionSlug, bool isDeny, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);
    Task<Result> AssignUserRoleAsync(int userId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);
    Task<Result> RemoveUserRoleAsync(int userId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);
    Task<Result> AssignGroupRoleAsync(int groupId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);
    Task<Result> RemoveGroupRoleAsync(int groupId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken = default);
}
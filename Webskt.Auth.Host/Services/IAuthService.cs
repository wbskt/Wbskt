using Webskt.Auth.Host.Models;
using Webskt.Common.Abstraction.Models.Auth;

namespace Webskt.Auth.Host.Services;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(string email, string password, string ipAddress, CancellationToken cancellationToken = default);
    Task<LoginResponse> RefreshTokenAsync(string token, string ipAddress, CancellationToken cancellationToken = default);
    Task<bool> VerifyPermissionAsync(int userId, string permissionSlug, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<PermissionResponse>> GetPermissionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RoleResponse>> GetRolesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<GroupResponse>> GetGroupsAsync(CancellationToken cancellationToken = default);
    Task RegisterUserAsync(string username, string email, string password, CancellationToken cancellationToken = default);
    Task CreateRoleAsync(string name, string description, CancellationToken cancellationToken = default);
    Task CreateGroupAsync(string name, int? parentGroupId, CancellationToken cancellationToken = default);
    Task AddUserToGroupAsync(int userId, int groupId, CancellationToken cancellationToken = default);
    Task CreatePermissionAsync(string slug, string description, CancellationToken cancellationToken = default);
    Task GrantRolePermissionAsync(int roleId, string permissionSlug, bool isDeny, CancellationToken cancellationToken = default);
    Task GrantUserPermissionAsync(int userId, string permissionSlug, bool isDeny, CancellationToken cancellationToken = default);
}
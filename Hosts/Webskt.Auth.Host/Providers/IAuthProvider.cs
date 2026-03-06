using Webskt.Auth.Host.Models;
using Webskt.Foundation.Abstraction;

namespace Webskt.Auth.Host.Providers;

internal interface IAuthProvider : IReferenceProvider
{
    Task<User> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<User> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<int> InsertUserAsync(User user, CancellationToken cancellationToken = default);
    Task InsertRefreshTokenAsync(RefreshToken token, string ipAddress, CancellationToken cancellationToken = default);
    Task<RefreshToken> GetRefreshTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<bool> VerifyPermissionAsync(int userId, string permissionSlug, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<PermissionResponse>> GetPermissionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<RoleResponse>> GetRolesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<GroupResponse>> GetGroupsAsync(CancellationToken cancellationToken = default);
    Task InsertRoleAsync(string name, string description, CancellationToken cancellationToken = default);
    Task InsertGroupAsync(string name, int? parentGroupId, CancellationToken cancellationToken = default);
    Task InsertUserGroupAsync(int userId, int groupId, CancellationToken cancellationToken = default);
    Task InsertPermissionAsync(string slug, string description, CancellationToken cancellationToken = default);
    Task GrantRolePermissionAsync(int roleId, string permissionSlug, bool isDeny, CancellationToken cancellationToken = default);
    Task GrantUserPermissionAsync(int userId, string permissionSlug, bool isDeny, CancellationToken cancellationToken = default);
}
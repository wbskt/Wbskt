using Webskt.Auth.Host.Models;
using Webskt.Common.Abstraction.Interfaces;

namespace Webskt.Auth.Host.Providers;

internal interface IAuthProvider : IReferenceProvider
{
    Task<User> GetByEmailAsync(string email);
    Task<User> GetByIdAsync(int id);
    Task<int> InsertUserAsync(User user);
    Task InsertRefreshTokenAsync(RefreshToken token, string ipAddress);
    Task<RefreshToken> GetRefreshTokenAsync(string token);
    Task<bool> VerifyPermissionAsync(int userId, string permissionSlug);
    Task<IReadOnlyCollection<PermissionResponse>> GetPermissionsAsync();
    Task<IReadOnlyCollection<RoleResponse>> GetRolesAsync();
    Task<IReadOnlyCollection<GroupResponse>> GetGroupsAsync();
    Task InsertRoleAsync(string name, string description);
    Task InsertGroupAsync(string name, int? parentGroupId);
    Task InsertUserGroupAsync(int userId, int groupId);
    Task InsertPermissionAsync(string slug, string description);
    Task GrantRolePermissionAsync(int roleId, string permissionSlug, bool isDeny);
    Task GrantUserPermissionAsync(int userId, string permissionSlug, bool isDeny);
}
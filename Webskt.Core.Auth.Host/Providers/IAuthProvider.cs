using Webskt.Core.Auth.Host.Models;

namespace Webskt.Core.Auth.Host.Providers;

internal interface IAuthProvider
{
    Task<User> GetByEmailAsync(string email);
    Task<User> GetByIdAsync(int id);
    Task<int> InsertUserAsync(User user);
    Task InsertRefreshTokenAsync(RefreshToken token, string ipAddress);
    Task<RefreshToken> GetRefreshTokenAsync(string token);
    Task<bool> VerifyPermissionAsync(int userId, string permissionSlug);
    Task InsertRoleAsync(string name, string description);
    Task InsertGroupAsync(string name, int? parentGroupId);
    Task InsertUserGroupAsync(int userId, int groupId);
    Task InsertPermissionAsync(string slug, string description);
    Task GrantRolePermissionAsync(int roleId, string permissionSlug, bool isDeny);
    Task GrantUserPermissionAsync(int userId, string permissionSlug, bool isDeny);
}
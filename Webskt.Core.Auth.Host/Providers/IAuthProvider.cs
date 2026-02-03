using Webskt.Core.Auth.Host.Models;

namespace Webskt.Core.Auth.Host.Providers;

public interface IAuthProvider
{
    Task<User?> GetUserByEmailAsync(string email);
    Task<User?> GetUserByIdAsync(int id);
    Task<int> CreateUserAsync(User user);
    Task SaveRefreshTokenAsync(RefreshToken token, string ipAddress);
    Task<RefreshToken?> GetRefreshTokenAsync(string token);
    Task<bool> CheckPermissionAsync(int userId, string permissionSlug);
    Task CreateRoleAsync(string name, string description);
    Task CreateGroupAsync(string name, int? parentGroupId);
    Task AddUserToGroupAsync(int userId, int groupId);
    Task CreatePermissionAsync(string slug, string description);
    Task GrantRolePermissionAsync(int roleId, string permissionSlug, bool isDeny);
    Task GrantUserPermissionAsync(int userId, string permissionSlug, bool isDeny);
}

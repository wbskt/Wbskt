using Webskt.Core.Auth.Host.Models;

namespace Webskt.Core.Auth.Host.Services;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(string email, string password, string ipAddress);
    Task<LoginResponse> RefreshTokenAsync(string token, string ipAddress);
    Task<bool> ValidatePermissionAsync(int userId, string permissionSlug);
    Task RegisterUserAsync(string username, string email, string password);
    Task CreateRoleAsync(string name, string description);
    Task CreateGroupAsync(string name, int? parentGroupId);
    Task AddUserToGroupAsync(int userId, int groupId);
    Task CreatePermissionAsync(string slug, string description);
    Task GrantRolePermissionAsync(int roleId, string permissionSlug, bool isDeny);
    Task GrantUserPermissionAsync(int userId, string permissionSlug, bool isDeny);
}

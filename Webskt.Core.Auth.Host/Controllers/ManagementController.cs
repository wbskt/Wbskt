using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Webskt.Core.Auth.Host.Services;

namespace Webskt.Core.Auth.Host.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class ManagementController : ControllerBase
{
    private readonly IAuthService _authService;

    public ManagementController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("roles")]
    public async Task<string> CreateRole(string name, string description)
    {
        await _authService.CreateRoleAsync(name, description);
        return "Role created";
    }

    [HttpPost("groups")]
    public async Task<string> CreateGroup(string name, int? parentGroupId)
    {
        await _authService.CreateGroupAsync(name, parentGroupId);
        return "Group created";
    }

    [HttpPost("users/{userId}/groups/{groupId}")]
    public async Task<string> AddUserToGroup(int userId, int groupId)
    {
        await _authService.AddUserToGroupAsync(userId, groupId);
        return "User added to group";
    }

    [HttpPost("permissions")]
    public async Task<string> CreatePermission(string slug, string description)
    {
        await _authService.CreatePermissionAsync(slug, description);
        return "Permission created";
    }

    [HttpPost("roles/{roleId}/permissions")]
    public async Task<string> GrantRolePermission(int roleId, string slug, bool isDeny = false)
    {
        await _authService.GrantRolePermissionAsync(roleId, slug, isDeny);
        return "Permission granted to role";
    }

    [HttpPost("users/{userId}/permissions")]
    public async Task<string> GrantUserPermission(int userId, string slug, bool isDeny = false)
    {
        await _authService.GrantUserPermissionAsync(userId, slug, isDeny);
        return "Permission granted to user";
    }
}
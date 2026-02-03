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
    public async Task<IActionResult> CreateRole(string name, string description)
    {
        await _authService.CreateRoleAsync(name, description);
        return Ok(new { message = "Role created" });
    }

    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup(string name, int? parentGroupId)
    {
        await _authService.CreateGroupAsync(name, parentGroupId);
        return Ok(new { message = "Group created" });
    }

    [HttpPost("users/{userId}/groups/{groupId}")]
    public async Task<IActionResult> AddUserToGroup(int userId, int groupId)
    {
        await _authService.AddUserToGroupAsync(userId, groupId);
        return Ok(new { message = "User added to group" });
    }

    [HttpPost("permissions")]
    public async Task<IActionResult> CreatePermission(string slug, string description)
    {
        await _authService.CreatePermissionAsync(slug, description);
        return Ok(new { message = "Permission created" });
    }

    [HttpPost("roles/{roleId}/permissions")]
    public async Task<IActionResult> GrantRolePermission(int roleId, string slug, bool isDeny = false)
    {
        await _authService.GrantRolePermissionAsync(roleId, slug, isDeny);
        return Ok(new { message = "Permission granted to role" });
    }

    [HttpPost("users/{userId}/permissions")]
    public async Task<IActionResult> GrantUserPermission(int userId, string slug, bool isDeny = false)
    {
        await _authService.GrantUserPermissionAsync(userId, slug, isDeny);
        return Ok(new { message = "Permission granted to user" });
    }
}

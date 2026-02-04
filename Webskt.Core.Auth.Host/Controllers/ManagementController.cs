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
    public async Task CreateRole(string name, string description)
    {
        await _authService.CreateRoleAsync(name, description);
    }

    [HttpPost("groups")]
    public async Task CreateGroup(string name, int? parentGroupId)
    {
        await _authService.CreateGroupAsync(name, parentGroupId);
    }

    [HttpPost("users/{userId}/groups/{groupId}")]
    public async Task AddUserToGroup(int userId, int groupId)
    {
        await _authService.AddUserToGroupAsync(userId, groupId);
    }

    [HttpPost("permissions")]
    public async Task CreatePermission(string slug, string description)
    {
        await _authService.CreatePermissionAsync(slug, description);
    }

    [HttpPost("roles/{roleId}/permissions")]
    public async Task GrantRolePermission(int roleId, string slug, bool isDeny = false)
    {
        await _authService.GrantRolePermissionAsync(roleId, slug, isDeny);
    }

    [HttpPost("users/{userId}/permissions")]
    public async Task GrantUserPermission(int userId, string slug, bool isDeny = false)
    {
        await _authService.GrantUserPermissionAsync(userId, slug, isDeny);
    }
}

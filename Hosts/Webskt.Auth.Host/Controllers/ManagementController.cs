using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Webskt.Auth.Host.Models;
using Webskt.Auth.Host.Services;

namespace Webskt.Auth.Host.Controllers;

[Route("api/management")]
[ApiController]
[Authorize]
public class ManagementController : ControllerBase
{
    private readonly IAuthService _authService;

    public ManagementController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpGet("roles")]
    public async Task<IReadOnlyCollection<RoleResponse>> GetRoles(CancellationToken cancellationToken)
    {
        return await _authService.GetRolesAsync(cancellationToken);
    }

    [HttpGet("groups")]
    public async Task<IReadOnlyCollection<GroupResponse>> GetGroups(CancellationToken cancellationToken)
    {
        return await _authService.GetGroupsAsync(cancellationToken);
    }

    [HttpGet("permissions")]
    public async Task<IReadOnlyCollection<PermissionResponse>> GetPermissions(CancellationToken cancellationToken)
    {
        return await _authService.GetPermissionsAsync(cancellationToken);
    }

    [HttpPost("roles")]
    public async Task CreateRole(string name, string description, CancellationToken cancellationToken)
    {
        await _authService.CreateRoleAsync(name, description, cancellationToken);
    }

    [HttpPost("groups")]
    public async Task CreateGroup(string name, int? parentGroupId, CancellationToken cancellationToken)
    {
        await _authService.CreateGroupAsync(name, parentGroupId, cancellationToken);
    }

    [HttpPost("users/{userId}/groups/{groupId}")]
    public async Task AddUserToGroup(int userId, int groupId, CancellationToken cancellationToken)
    {
        await _authService.AddUserToGroupAsync(userId, groupId, cancellationToken);
    }

    [HttpPost("permissions")]
    public async Task CreatePermission(string slug, string description, CancellationToken cancellationToken)
    {
        await _authService.CreatePermissionAsync(slug, description, cancellationToken);
    }

    [HttpPost("roles/{roleId}/permissions")]
    public async Task GrantRolePermission(int roleId, string slug, CancellationToken cancellationToken, bool isDeny = false)
    {
        await _authService.GrantRolePermissionAsync(roleId, slug, isDeny, cancellationToken);
    }

    [HttpPost("users/{userId}/permissions")]
    public async Task GrantUserPermission(int userId, string slug, CancellationToken cancellationToken, bool isDeny = false)
    {
        await _authService.GrantUserPermissionAsync(userId, slug, isDeny, cancellationToken);
    }
}

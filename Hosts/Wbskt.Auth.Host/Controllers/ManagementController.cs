using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services;

namespace Wbskt.Auth.Host.Controllers;

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

    /// <summary>
    /// Retrieves all roles defined in the system.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of role responses.</returns>
    [HttpGet("roles")]
    public async Task<IReadOnlyCollection<RoleResponse>> GetRoles(CancellationToken cancellationToken)
    {
        return await _authService.GetRolesAsync(cancellationToken);
    }

    /// <summary>
    /// Retrieves all user groups defined in the system.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of group responses.</returns>
    [HttpGet("groups")]
    public async Task<IReadOnlyCollection<GroupResponse>> GetGroups(CancellationToken cancellationToken)
    {
        return await _authService.GetGroupsAsync(cancellationToken);
    }

    /// <summary>
    /// Retrieves all permissions available in the system.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of permission responses.</returns>
    [HttpGet("permissions")]
    public async Task<IReadOnlyCollection<PermissionResponse>> GetPermissions(CancellationToken cancellationToken)
    {
        return await _authService.GetPermissionsAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a new system role.
    /// </summary>
    /// <param name="name">The name of the role.</param>
    /// <param name="description">The role's description.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("roles")]
    public async Task CreateRole(string name, string description, CancellationToken cancellationToken)
    {
        await _authService.CreateRoleAsync(name, description, cancellationToken);
    }

    /// <summary>
    /// Creates a new user group, optionally nested under a parent group.
    /// </summary>
    /// <param name="name">The name of the group.</param>
    /// <param name="parentGroupId">The ID of the parent group, if any.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("groups")]
    public async Task CreateGroup(string name, int? parentGroupId, CancellationToken cancellationToken)
    {
        await _authService.CreateGroupAsync(name, parentGroupId, cancellationToken);
    }

    /// <summary>
    /// Assigns a specific user to a group.
    /// </summary>
    /// <param name="userId">The ID of the user.</param>
    /// <param name="groupId">The ID of the group.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("users/{userId}/groups/{groupId}")]
    public async Task AddUserToGroup(int userId, int groupId, CancellationToken cancellationToken)
    {
        await _authService.AddUserToGroupAsync(userId, groupId, cancellationToken);
    }

    /// <summary>
    /// Defines a new permission in the system.
    /// </summary>
    /// <param name="slug">The unique slug representing the permission (e.g., 'users.read').</param>
    /// <param name="description">A description of what the permission allows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("permissions")]
    public async Task CreatePermission(string slug, string description, CancellationToken cancellationToken)
    {
        await _authService.CreatePermissionAsync(slug, description, cancellationToken);
    }

    /// <summary>
    /// Grants or denies a specific permission to a role.
    /// </summary>
    /// <param name="roleId">The ID of the role.</param>
    /// <param name="slug">The slug of the permission.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="isDeny">If true, explicitly denies the permission.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("roles/{roleId}/permissions")]
    public async Task GrantRolePermission(int roleId, string slug, CancellationToken cancellationToken, bool isDeny = false)
    {
        await _authService.GrantRolePermissionAsync(roleId, slug, isDeny, cancellationToken);
    }

    /// <summary>
    /// Grants or denies a specific permission directly to a user (overriding role permissions).
    /// </summary>
    /// <param name="userId">The ID of the user.</param>
    /// <param name="slug">The slug of the permission.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="isDeny">If true, explicitly denies the permission.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("users/{userId}/permissions")]
    public async Task GrantUserPermission(int userId, string slug, CancellationToken cancellationToken, bool isDeny = false)
    {
        await _authService.GrantUserPermissionAsync(userId, slug, isDeny, cancellationToken);
    }
}

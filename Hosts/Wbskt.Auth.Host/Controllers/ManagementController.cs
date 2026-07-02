using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Controllers;

[Route("api/management")]
[ApiController]
[Authorize]
public class ManagementController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<ManagementController> _logger;

    public ManagementController(IAuthService authService, ILogger<ManagementController> _logger)
    {
        this._authService = authService;
        this._logger = _logger;
    }

    /// <summary>
    /// Retrieves all roles defined in the system.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of role responses.</returns>
    [HttpGet("roles")]
    public async Task<ActionResult<IReadOnlyCollection<RoleResponse>>> GetRoles(CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: GetRoles requested");
        var result = await _authService.GetRolesAsync(cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Retrieves all user groups defined in the system.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of group responses.</returns>
    [HttpGet("groups")]
    public async Task<ActionResult<IReadOnlyCollection<GroupResponse>>> GetGroups(CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: GetGroups requested");
        var result = await _authService.GetGroupsAsync(cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Retrieves all permissions available in the system.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of permission responses.</returns>
    [HttpGet("permissions")]
    public async Task<ActionResult<IReadOnlyCollection<PermissionResponse>>> GetPermissions(CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: GetPermissions requested");
        var result = await _authService.GetPermissionsAsync(cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Creates a new system role.
    /// </summary>
    /// <param name="name">The name of the role.</param>
    /// <param name="description">The role's description.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("roles")]
    public async Task<IActionResult> CreateRole(string name, string description, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: CreateRole requested (Name: '{RoleName}')", name);
        var result = await _authService.CreateRoleAsync(name, description, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Creates a new user group, optionally nested under a parent group.
    /// </summary>
    /// <param name="name">The name of the group.</param>
    /// <param name="parentGroupId">The ID of the parent group, if any.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup(string name, int? parentGroupId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: CreateGroup requested (Name: '{GroupName}', ParentGroupId: {ParentGroupId})", name, parentGroupId);
        var result = await _authService.CreateGroupAsync(name, parentGroupId, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Assigns a specific user to a group.
    /// </summary>
    /// <param name="userId">The ID of the user.</param>
    /// <param name="groupId">The ID of the group.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("users/{userId}/groups/{groupId}")]
    public async Task<IActionResult> AddUserToGroup(int userId, int groupId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: AddUserToGroup requested for UserId {UserId} and GroupId {GroupId}", userId, groupId);
        var result = await _authService.AddUserToGroupAsync(userId, groupId, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Defines a new permission in the system.
    /// </summary>
    /// <param name="slug">The unique slug representing the permission (e.g., 'users.read').</param>
    /// <param name="description">A description of what the permission allows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("permissions")]
    public async Task<IActionResult> CreatePermission(string slug, string description, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: CreatePermission requested (Slug: '{PermissionSlug}')", slug);
        var result = await _authService.CreatePermissionAsync(slug, description, cancellationToken);
        return MapResult(result);
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
    public async Task<IActionResult> GrantRolePermission(int roleId, string slug, CancellationToken cancellationToken, bool isDeny = false)
    {
        _logger.LogInformation("API: GrantRolePermission requested for RoleId {RoleId}, Slug: '{PermissionSlug}' (IsDeny: {IsDeny})", roleId, slug, isDeny);
        var result = await _authService.GrantRolePermissionAsync(roleId, slug, isDeny, cancellationToken);
        return MapResult(result);
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
    public async Task<IActionResult> GrantUserPermission(int userId, string slug, CancellationToken cancellationToken, bool isDeny = false)
    {
        _logger.LogInformation("API: GrantUserPermission requested for UserId {UserId}, Slug: '{PermissionSlug}' (IsDeny: {IsDeny})", userId, slug, isDeny);
        var result = await _authService.GrantUserPermissionAsync(userId, slug, isDeny, cancellationToken);
        return MapResult(result);
    }

    private IActionResult MapResult(Result result)
    {
        if (result.IsSuccess)
        {
            return NoContent();
        }

        return MapError(result.Error);
    }

    private ActionResult<T> MapResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return MapError(result.Error);
    }

    private ActionResult MapError(Error error)
    {
        _logger.LogWarning("API Response Failure: Code={ErrorCode}, Message={ErrorMessage}", error.Code, error.Message);
        return error.Type switch
        {
            ErrorType.Validation => BadRequest(error),
            ErrorType.NotFound => NotFound(error),
            ErrorType.Conflict => Conflict(error),
            ErrorType.Unauthorized => Unauthorized(error),
            _ => BadRequest(error)
        };
    }
}

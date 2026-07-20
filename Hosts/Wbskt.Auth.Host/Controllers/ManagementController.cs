using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services;
using Wbskt.Infrastructure;
using Wbskt.Primitives.Constants;
using Wbskt.Primitives.Models;

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
    /// Retrieves the tenants the current user belongs to.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of tenant responses.</returns>
    [HttpGet("tenants")]
    public async Task<ActionResult<IReadOnlyCollection<TenantResponse>>> GetTenants(CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: GetTenants requested");
        var userIdResult = GetCurrentUserId();
        if (userIdResult.IsFailure)
        {
            return MapError(userIdResult.Error);
        }

        var result = await _authService.GetTenantsForUserAsync(userIdResult.Value, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Retrieves all roles defined in a tenant.
    /// </summary>
    /// <param name="tenantId">The ID of the tenant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of role responses.</returns>
    [HttpGet("roles")]
    public async Task<ActionResult<IReadOnlyCollection<RoleResponse>>> GetRoles(int tenantId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: GetRoles requested (TenantId: {TenantId})", tenantId);
        var gate = await EnsureTenantPermissionAsync(tenantId, Permissions.RolesManage, cancellationToken);
        if (gate.IsFailure)
        {
            return MapError(gate.Error);
        }

        var result = await _authService.GetRolesAsync(tenantId, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Retrieves all user groups defined in a tenant.
    /// </summary>
    /// <param name="tenantId">The ID of the tenant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of group responses.</returns>
    [HttpGet("groups")]
    public async Task<ActionResult<IReadOnlyCollection<GroupResponse>>> GetGroups(int tenantId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: GetGroups requested (TenantId: {TenantId})", tenantId);
        var gate = await EnsureTenantPermissionAsync(tenantId, Permissions.UsersManage, cancellationToken);
        if (gate.IsFailure)
        {
            return MapError(gate.Error);
        }

        var result = await _authService.GetGroupsAsync(tenantId, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Retrieves all permissions available in the system.
    /// </summary>
    /// <param name="tenantId">The ID of the tenant used for the permission check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A collection of permission responses.</returns>
    [HttpGet("permissions")]
    public async Task<ActionResult<IReadOnlyCollection<PermissionResponse>>> GetPermissions(int tenantId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: GetPermissions requested (TenantId: {TenantId})", tenantId);
        var gate = await EnsureTenantPermissionAsync(tenantId, Permissions.RolesManage, cancellationToken);
        if (gate.IsFailure)
        {
            return MapError(gate.Error);
        }

        var result = await _authService.GetPermissionsAsync(cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Creates a new role within a tenant.
    /// </summary>
    /// <param name="name">The name of the role.</param>
    /// <param name="description">The role's description.</param>
    /// <param name="tenantId">The ID of the tenant that owns the role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("roles")]
    public async Task<IActionResult> CreateRole(string name, string description, int tenantId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: CreateRole requested (Name: '{RoleName}', TenantId: {TenantId})", name, tenantId);
        var gate = await EnsureTenantPermissionAsync(tenantId, Permissions.RolesManage, cancellationToken);
        if (gate.IsFailure)
        {
            return MapError(gate.Error);
        }

        var result = await _authService.CreateRoleAsync(name, description, tenantId, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Creates a new user group within a tenant, optionally nested under a parent group.
    /// </summary>
    /// <param name="name">The name of the group.</param>
    /// <param name="parentGroupId">The ID of the parent group, if any.</param>
    /// <param name="tenantId">The ID of the tenant that owns the group.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("groups")]
    public async Task<IActionResult> CreateGroup(string name, int? parentGroupId, int tenantId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: CreateGroup requested (Name: '{GroupName}', ParentGroupId: {ParentGroupId}, TenantId: {TenantId})", name, parentGroupId, tenantId);
        var gate = await EnsureTenantPermissionAsync(tenantId, Permissions.UsersManage, cancellationToken);
        if (gate.IsFailure)
        {
            return MapError(gate.Error);
        }

        var result = await _authService.CreateGroupAsync(name, parentGroupId, tenantId, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Assigns a specific user to a group.
    /// </summary>
    /// <param name="userId">The ID of the user.</param>
    /// <param name="groupId">The ID of the group.</param>
    /// <param name="tenantId">The ID of the tenant that owns the group.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("users/{userId}/groups/{groupId}")]
    public async Task<IActionResult> AddUserToGroup(int userId, int groupId, int tenantId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: AddUserToGroup requested for UserId {UserId} and GroupId {GroupId} (TenantId: {TenantId})", userId, groupId, tenantId);
        var gate = await EnsureTenantPermissionAsync(tenantId, Permissions.UsersManage, cancellationToken);
        if (gate.IsFailure)
        {
            return MapError(gate.Error);
        }

        var result = await _authService.AddUserToGroupAsync(userId, groupId, tenantId, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Defines a new permission in the system.
    /// </summary>
    /// <param name="slug">The unique slug representing the permission (e.g., 'users.read').</param>
    /// <param name="description">A description of what the permission allows.</param>
    /// <param name="tenantId">The ID of the tenant used for the permission check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("permissions")]
    public async Task<IActionResult> CreatePermission(string slug, string description, int tenantId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: CreatePermission requested (Slug: '{PermissionSlug}', TenantId: {TenantId})", slug, tenantId);
        var gate = await EnsureTenantPermissionAsync(tenantId, Permissions.RolesManage, cancellationToken);
        if (gate.IsFailure)
        {
            return MapError(gate.Error);
        }

        var result = await _authService.CreatePermissionAsync(slug, description, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Grants or denies a specific permission to a role.
    /// </summary>
    /// <param name="roleId">The ID of the role.</param>
    /// <param name="slug">The slug of the permission.</param>
    /// <param name="tenantId">The ID of the tenant that owns the role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="isDeny">If true, explicitly denies the permission.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("roles/{roleId}/permissions")]
    public async Task<IActionResult> GrantRolePermission(int roleId, string slug, int tenantId, CancellationToken cancellationToken, bool isDeny = false)
    {
        _logger.LogInformation("API: GrantRolePermission requested for RoleId {RoleId}, Slug: '{PermissionSlug}' (IsDeny: {IsDeny}, TenantId: {TenantId})", roleId, slug, isDeny, tenantId);
        var gate = await EnsureTenantPermissionAsync(tenantId, Permissions.RolesManage, cancellationToken);
        if (gate.IsFailure)
        {
            return MapError(gate.Error);
        }

        var result = await _authService.GrantRolePermissionAsync(roleId, slug, isDeny, tenantId, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Grants or denies a specific permission directly to a user (overriding role permissions)
    /// at a scope: tenant-wide when no workspace is given, otherwise scoped to that workspace.
    /// </summary>
    /// <param name="userId">The ID of the user.</param>
    /// <param name="slug">The slug of the permission.</param>
    /// <param name="tenantId">The ID of the tenant the assignment belongs to.</param>
    /// <param name="workspaceId">Optional workspace scope; null means tenant-wide.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="isDeny">If true, explicitly denies the permission.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("users/{userId}/permissions")]
    public async Task<IActionResult> GrantUserPermission(int userId, string slug, int tenantId, int? workspaceId, CancellationToken cancellationToken, bool isDeny = false)
    {
        _logger.LogInformation("API: GrantUserPermission requested for UserId {UserId}, Slug: '{PermissionSlug}' (IsDeny: {IsDeny}, TenantId: {TenantId}, WorkspaceId: {WorkspaceId})", userId, slug, isDeny, tenantId, workspaceId);
        var gate = await EnsureTenantPermissionAsync(tenantId, Permissions.RolesManage, cancellationToken);
        if (gate.IsFailure)
        {
            return MapError(gate.Error);
        }

        var result = await _authService.GrantUserPermissionAsync(userId, slug, isDeny, tenantId, workspaceId, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Assigns a role to a user at a scope: tenant-wide when no workspace is given,
    /// otherwise scoped to that workspace.
    /// </summary>
    /// <param name="userId">The ID of the user.</param>
    /// <param name="roleId">The ID of the role.</param>
    /// <param name="tenantId">The ID of the tenant that owns the role.</param>
    /// <param name="workspaceId">Optional workspace scope; null means tenant-wide.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("users/{userId}/roles/{roleId}")]
    public async Task<IActionResult> AssignUserRole(int userId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: AssignUserRole requested for UserId {UserId}, RoleId {RoleId} (TenantId: {TenantId}, WorkspaceId: {WorkspaceId})", userId, roleId, tenantId, workspaceId);
        var gate = await EnsureTenantPermissionAsync(tenantId, Permissions.RolesManage, cancellationToken);
        if (gate.IsFailure)
        {
            return MapError(gate.Error);
        }

        var result = await _authService.AssignUserRoleAsync(userId, roleId, tenantId, workspaceId, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Removes a role assignment from a user at a scope: tenant-wide when no workspace is given,
    /// otherwise scoped to that workspace.
    /// </summary>
    /// <param name="userId">The ID of the user.</param>
    /// <param name="roleId">The ID of the role.</param>
    /// <param name="tenantId">The ID of the tenant that owns the role.</param>
    /// <param name="workspaceId">Optional workspace scope; null means tenant-wide.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpDelete("users/{userId}/roles/{roleId}")]
    public async Task<IActionResult> RemoveUserRole(int userId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: RemoveUserRole requested for UserId {UserId}, RoleId {RoleId} (TenantId: {TenantId}, WorkspaceId: {WorkspaceId})", userId, roleId, tenantId, workspaceId);
        var gate = await EnsureTenantPermissionAsync(tenantId, Permissions.RolesManage, cancellationToken);
        if (gate.IsFailure)
        {
            return MapError(gate.Error);
        }

        var result = await _authService.RemoveUserRoleAsync(userId, roleId, tenantId, workspaceId, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Assigns a role to a group at a scope: tenant-wide when no workspace is given,
    /// otherwise scoped to that workspace.
    /// </summary>
    /// <param name="groupId">The ID of the group.</param>
    /// <param name="roleId">The ID of the role.</param>
    /// <param name="tenantId">The ID of the tenant that owns the role and group.</param>
    /// <param name="workspaceId">Optional workspace scope; null means tenant-wide.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpPost("groups/{groupId}/roles/{roleId}")]
    public async Task<IActionResult> AssignGroupRole(int groupId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: AssignGroupRole requested for GroupId {GroupId}, RoleId {RoleId} (TenantId: {TenantId}, WorkspaceId: {WorkspaceId})", groupId, roleId, tenantId, workspaceId);
        var gate = await EnsureTenantPermissionAsync(tenantId, Permissions.RolesManage, cancellationToken);
        if (gate.IsFailure)
        {
            return MapError(gate.Error);
        }

        var result = await _authService.AssignGroupRoleAsync(groupId, roleId, tenantId, workspaceId, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Removes a role assignment from a group at a scope: tenant-wide when no workspace is given,
    /// otherwise scoped to that workspace.
    /// </summary>
    /// <param name="groupId">The ID of the group.</param>
    /// <param name="roleId">The ID of the role.</param>
    /// <param name="tenantId">The ID of the tenant that owns the role and group.</param>
    /// <param name="workspaceId">Optional workspace scope; null means tenant-wide.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [HttpDelete("groups/{groupId}/roles/{roleId}")]
    public async Task<IActionResult> RemoveGroupRole(int groupId, int roleId, int tenantId, int? workspaceId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: RemoveGroupRole requested for GroupId {GroupId}, RoleId {RoleId} (TenantId: {TenantId}, WorkspaceId: {WorkspaceId})", groupId, roleId, tenantId, workspaceId);
        var gate = await EnsureTenantPermissionAsync(tenantId, Permissions.RolesManage, cancellationToken);
        if (gate.IsFailure)
        {
            return MapError(gate.Error);
        }

        var result = await _authService.RemoveGroupRoleAsync(groupId, roleId, tenantId, workspaceId, cancellationToken);
        return MapResult(result);
    }

    /// <summary>
    /// Verifies the caller holds the given permission tenant-wide (WorkspaceId = null scope)
    /// before allowing a management operation.
    /// </summary>
    private async Task<Result> EnsureTenantPermissionAsync(int tenantId, PermissionSlug permission, CancellationToken cancellationToken)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.IsFailure)
        {
            return Result.Failure(userIdResult.Error);
        }

        var verifyResult = await _authService.VerifyPermissionAsync(userIdResult.Value, tenantId, null, permission, cancellationToken);
        if (verifyResult.IsFailure)
        {
            return Result.Failure(verifyResult.Error);
        }

        if (!verifyResult.Value)
        {
            _logger.LogWarning("Management operation denied: UserId {UserId} lacks tenant-wide '{Permission}' in TenantId {TenantId}", userIdResult.Value, permission, tenantId);
            return Result.Failure(Error.Unauthorized("PERMISSION_UNAUTHORIZED", $"user does not have permission(s) {permission}"));
        }

        return Result.Success();
    }

    private Result<int> GetCurrentUserId()
    {
        var userIdString = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out var userId))
        {
            return Result<int>.Failure(Error.Unauthorized("AUTH_UNAUTHORIZED", "Unauthorized access."));
        }
        return Result<int>.Success(userId);
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
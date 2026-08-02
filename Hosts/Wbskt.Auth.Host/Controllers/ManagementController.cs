using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Services;
using Wbskt.Infrastructure;
using Wbskt.Models;

namespace Wbskt.Auth.Host.Controllers;

/// <summary>
/// Tenant administration. Everything is addressed by public <c>Guid</c> reference and scoped to a
/// tenant in the route, matching the workspace-scoped controllers in the management host.
/// The permission gate lives in <see cref="IManagementService"/>, alongside the reference
/// resolution it depends on.
/// </summary>
[Route("api/tenants")]
[ApiController]
[Authorize]
public class ManagementController : ApiControllerBase
{
    private const int MaxPageSize = 200;

    private readonly IManagementService _managementService;
    private readonly ILogger<ManagementController> _logger;

    public ManagementController(IManagementService managementService, ILogger<ManagementController> logger)
    {
        _managementService = managementService;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves the tenants the current user belongs to. This is the entry point for every other
    /// endpoint here, since they are all addressed by tenant reference.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<TenantResponse>>> GetTenants(CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: GetTenants requested");

        var caller = CurrentUserId();
        if (caller.IsFailure)
        {
            return MapError(caller.Error);
        }

        return MapResult(await _managementService.GetTenantsForUserAsync(caller.Value, cancellationToken));
    }

    // ----- Roles -------------------------------------------------------------------------------

    /// <summary>Lists the roles defined in a tenant. Requires <c>roles.read</c>.</summary>
    [HttpGet("{tenantRef:guid}/roles")]
    public async Task<ActionResult<ListResponse<RoleResponse>>> GetRoles(Guid tenantRef, [FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("API: GetRoles requested (TenantRef: {TenantRef})", tenantRef);
        return await PagedAsync(tenantRef, skip, take, (callerId, s, t) => _managementService.GetRolesAsync(callerId, tenantRef, s, t, cancellationToken));
    }

    /// <summary>Creates a role. Requires <c>roles.manage</c>.</summary>
    [HttpPost("{tenantRef:guid}/roles")]
    public async Task<ActionResult<RoleResponse>> CreateRole(Guid tenantRef, [FromBody] CreateRoleRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: CreateRole requested (Name: '{RoleName}', TenantRef: {TenantRef})", request.Name, tenantRef);

        var caller = CurrentUserId();
        if (caller.IsFailure)
        {
            return MapError(caller.Error);
        }

        return MapResult(await _managementService.CreateRoleAsync(caller.Value, tenantRef, request, cancellationToken));
    }

    /// <summary>Renames a role or changes its description. Requires <c>roles.manage</c>.</summary>
    [HttpPut("{tenantRef:guid}/roles/{roleRef:guid}")]
    public async Task<IActionResult> UpdateRole(Guid tenantRef, Guid roleRef, [FromBody] UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: UpdateRole requested (RoleRef: {RoleRef}, TenantRef: {TenantRef})", roleRef, tenantRef);
        return await WithCallerAsync(callerId => _managementService.UpdateRoleAsync(callerId, tenantRef, roleRef, request, cancellationToken));
    }

    /// <summary>
    /// Deletes a role along with its permissions and every assignment of it. Requires <c>roles.manage</c>.
    /// </summary>
    [HttpDelete("{tenantRef:guid}/roles/{roleRef:guid}")]
    public async Task<IActionResult> DeleteRole(Guid tenantRef, Guid roleRef, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: DeleteRole requested (RoleRef: {RoleRef}, TenantRef: {TenantRef})", roleRef, tenantRef);
        return await WithCallerAsync(callerId => _managementService.DeleteRoleAsync(callerId, tenantRef, roleRef, cancellationToken));
    }

    /// <summary>Lists the permissions attached to a role. Requires <c>roles.read</c>.</summary>
    [HttpGet("{tenantRef:guid}/roles/{roleRef:guid}/permissions")]
    public async Task<ActionResult<IReadOnlyCollection<RolePermissionAssignmentResponse>>> GetRolePermissions(Guid tenantRef, Guid roleRef, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: GetRolePermissions requested (RoleRef: {RoleRef})", roleRef);

        var caller = CurrentUserId();
        if (caller.IsFailure)
        {
            return MapError(caller.Error);
        }

        return MapResult(await _managementService.GetRolePermissionsAsync(caller.Value, tenantRef, roleRef, cancellationToken));
    }

    /// <summary>
    /// Grants or denies a permission on a role. Role permissions are unscoped — the workspace is
    /// chosen when the role is assigned, so this body takes no <c>workspaceRef</c>.
    /// Requires <c>roles.manage</c>.
    /// </summary>
    [HttpPost("{tenantRef:guid}/roles/{roleRef:guid}/permissions")]
    public async Task<IActionResult> GrantRolePermission(Guid tenantRef, Guid roleRef, [FromBody] GrantRolePermissionRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: GrantRolePermission requested (RoleRef: {RoleRef}, Slug: '{Slug}', IsDeny: {IsDeny})", roleRef, request.Slug, request.IsDeny);
        return await WithCallerAsync(callerId => _managementService.GrantRolePermissionAsync(callerId, tenantRef, roleRef, request, cancellationToken));
    }

    /// <summary>
    /// Detaches a permission from a role, as distinct from denying it. Requires <c>roles.manage</c>.
    /// </summary>
    [HttpDelete("{tenantRef:guid}/roles/{roleRef:guid}/permissions/{slug}")]
    public async Task<IActionResult> RemoveRolePermission(Guid tenantRef, Guid roleRef, string slug, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: RemoveRolePermission requested (RoleRef: {RoleRef}, Slug: '{Slug}')", roleRef, slug);
        return await WithCallerAsync(callerId => _managementService.RemoveRolePermissionAsync(callerId, tenantRef, roleRef, slug, cancellationToken));
    }

    // ----- Groups ------------------------------------------------------------------------------

    /// <summary>Lists the groups defined in a tenant. Requires <c>users.read</c>.</summary>
    [HttpGet("{tenantRef:guid}/groups")]
    public async Task<ActionResult<ListResponse<GroupResponse>>> GetGroups(Guid tenantRef, [FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("API: GetGroups requested (TenantRef: {TenantRef})", tenantRef);
        return await PagedAsync(tenantRef, skip, take, (callerId, s, t) => _managementService.GetGroupsAsync(callerId, tenantRef, s, t, cancellationToken));
    }

    /// <summary>Creates a group, optionally nested under a parent. Requires <c>users.manage</c>.</summary>
    [HttpPost("{tenantRef:guid}/groups")]
    public async Task<ActionResult<GroupResponse>> CreateGroup(Guid tenantRef, [FromBody] CreateGroupRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: CreateGroup requested (Name: '{GroupName}', TenantRef: {TenantRef})", request.Name, tenantRef);

        var caller = CurrentUserId();
        if (caller.IsFailure)
        {
            return MapError(caller.Error);
        }

        return MapResult(await _managementService.CreateGroupAsync(caller.Value, tenantRef, request, cancellationToken));
    }

    /// <summary>Renames a group. Requires <c>users.manage</c>.</summary>
    [HttpPut("{tenantRef:guid}/groups/{groupRef:guid}")]
    public async Task<IActionResult> UpdateGroup(Guid tenantRef, Guid groupRef, [FromBody] UpdateGroupRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: UpdateGroup requested (GroupRef: {GroupRef})", groupRef);
        return await WithCallerAsync(callerId => _managementService.UpdateGroupAsync(callerId, tenantRef, groupRef, request, cancellationToken));
    }

    /// <summary>
    /// Deletes a group. Rejected if it still has child groups. Requires <c>users.manage</c>.
    /// </summary>
    [HttpDelete("{tenantRef:guid}/groups/{groupRef:guid}")]
    public async Task<IActionResult> DeleteGroup(Guid tenantRef, Guid groupRef, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: DeleteGroup requested (GroupRef: {GroupRef})", groupRef);
        return await WithCallerAsync(callerId => _managementService.DeleteGroupAsync(callerId, tenantRef, groupRef, cancellationToken));
    }

    /// <summary>Lists the roles assigned to a group. Requires <c>roles.read</c>.</summary>
    [HttpGet("{tenantRef:guid}/groups/{groupRef:guid}/roles")]
    public async Task<ActionResult<IReadOnlyCollection<RoleAssignmentResponse>>> GetGroupRoles(Guid tenantRef, Guid groupRef, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: GetGroupRoles requested (GroupRef: {GroupRef})", groupRef);

        var caller = CurrentUserId();
        if (caller.IsFailure)
        {
            return MapError(caller.Error);
        }

        return MapResult(await _managementService.GetGroupRolesAsync(caller.Value, tenantRef, groupRef, cancellationToken));
    }

    /// <summary>
    /// Assigns a role to a group. A null <c>workspaceRef</c> in the body means tenant-wide.
    /// Requires <c>roles.manage</c>.
    /// </summary>
    [HttpPost("{tenantRef:guid}/groups/{groupRef:guid}/roles/{roleRef:guid}")]
    public async Task<IActionResult> AssignGroupRole(Guid tenantRef, Guid groupRef, Guid roleRef, [FromBody] AssignmentScopeRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: AssignGroupRole requested (GroupRef: {GroupRef}, RoleRef: {RoleRef}, WorkspaceRef: {WorkspaceRef})", groupRef, roleRef, request.WorkspaceRef);
        return await WithCallerAsync(callerId => _managementService.AssignGroupRoleAsync(callerId, tenantRef, groupRef, roleRef, request.WorkspaceRef, cancellationToken));
    }

    /// <summary>Removes a role assignment from a group. Requires <c>roles.manage</c>.</summary>
    [HttpDelete("{tenantRef:guid}/groups/{groupRef:guid}/roles/{roleRef:guid}")]
    public async Task<IActionResult> RemoveGroupRole(Guid tenantRef, Guid groupRef, Guid roleRef, [FromQuery] Guid? workspaceRef, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: RemoveGroupRole requested (GroupRef: {GroupRef}, RoleRef: {RoleRef}, WorkspaceRef: {WorkspaceRef})", groupRef, roleRef, workspaceRef);
        return await WithCallerAsync(callerId => _managementService.RemoveGroupRoleAsync(callerId, tenantRef, groupRef, roleRef, workspaceRef, cancellationToken));
    }

    // ----- Permissions catalogue ---------------------------------------------------------------

    /// <summary>
    /// Lists the permission catalogue. The catalogue is global and code-defined, so the tenant here
    /// only scopes the permission check. Requires <c>roles.read</c>.
    /// </summary>
    [HttpGet("{tenantRef:guid}/permissions")]
    public async Task<ActionResult<ListResponse<PermissionResponse>>> GetPermissions(Guid tenantRef, [FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("API: GetPermissions requested (TenantRef: {TenantRef})", tenantRef);
        return await PagedAsync(tenantRef, skip, take, (callerId, s, t) => _managementService.GetPermissionsAsync(callerId, tenantRef, s, t, cancellationToken));
    }

    // ----- Members -----------------------------------------------------------------------------

    /// <summary>
    /// Lists the users in a tenant. This is how a caller discovers the user references every other
    /// endpoint here needs. Requires <c>users.read</c>.
    /// </summary>
    [HttpGet("{tenantRef:guid}/members")]
    public async Task<ActionResult<ListResponse<TenantMemberResponse>>> GetMembers(Guid tenantRef, [FromQuery] string? search, [FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("API: GetMembers requested (TenantRef: {TenantRef})", tenantRef);
        return await PagedAsync(tenantRef, skip, take, (callerId, s, t) => _managementService.GetMembersAsync(callerId, tenantRef, search, s, t, cancellationToken));
    }

    /// <summary>Lists the roles a user holds and the scope of each. Requires <c>roles.read</c>.</summary>
    [HttpGet("{tenantRef:guid}/members/{userRef:guid}/roles")]
    public async Task<ActionResult<IReadOnlyCollection<RoleAssignmentResponse>>> GetUserRoles(Guid tenantRef, Guid userRef, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: GetUserRoles requested (UserRef: {UserRef})", userRef);

        var caller = CurrentUserId();
        if (caller.IsFailure)
        {
            return MapError(caller.Error);
        }

        return MapResult(await _managementService.GetUserRolesAsync(caller.Value, tenantRef, userRef, cancellationToken));
    }

    /// <summary>
    /// Lists the permissions granted directly to a user. These override role-derived permissions in
    /// both directions, so this is where an unexpected allow or deny is diagnosed.
    /// Requires <c>roles.read</c>.
    /// </summary>
    [HttpGet("{tenantRef:guid}/members/{userRef:guid}/permissions")]
    public async Task<ActionResult<IReadOnlyCollection<UserPermissionAssignmentResponse>>> GetUserPermissions(Guid tenantRef, Guid userRef, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: GetUserPermissions requested (UserRef: {UserRef})", userRef);

        var caller = CurrentUserId();
        if (caller.IsFailure)
        {
            return MapError(caller.Error);
        }

        return MapResult(await _managementService.GetUserPermissionsAsync(caller.Value, tenantRef, userRef, cancellationToken));
    }

    /// <summary>Lists the groups a user belongs to. Requires <c>users.read</c>.</summary>
    [HttpGet("{tenantRef:guid}/members/{userRef:guid}/groups")]
    public async Task<ActionResult<IReadOnlyCollection<GroupMembershipResponse>>> GetUserGroups(Guid tenantRef, Guid userRef, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: GetUserGroups requested (UserRef: {UserRef})", userRef);

        var caller = CurrentUserId();
        if (caller.IsFailure)
        {
            return MapError(caller.Error);
        }

        return MapResult(await _managementService.GetUserGroupsAsync(caller.Value, tenantRef, userRef, cancellationToken));
    }

    /// <summary>Adds a user to a group. Requires <c>users.manage</c>.</summary>
    [HttpPost("{tenantRef:guid}/members/{userRef:guid}/groups/{groupRef:guid}")]
    public async Task<IActionResult> AddUserToGroup(Guid tenantRef, Guid userRef, Guid groupRef, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: AddUserToGroup requested (UserRef: {UserRef}, GroupRef: {GroupRef})", userRef, groupRef);
        return await WithCallerAsync(callerId => _managementService.AddUserToGroupAsync(callerId, tenantRef, userRef, groupRef, cancellationToken));
    }

    /// <summary>Removes a user from a group. Requires <c>users.manage</c>.</summary>
    [HttpDelete("{tenantRef:guid}/members/{userRef:guid}/groups/{groupRef:guid}")]
    public async Task<IActionResult> RemoveUserFromGroup(Guid tenantRef, Guid userRef, Guid groupRef, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: RemoveUserFromGroup requested (UserRef: {UserRef}, GroupRef: {GroupRef})", userRef, groupRef);
        return await WithCallerAsync(callerId => _managementService.RemoveUserFromGroupAsync(callerId, tenantRef, userRef, groupRef, cancellationToken));
    }

    /// <summary>
    /// Assigns a role to a user. A null <c>workspaceRef</c> in the body means tenant-wide.
    /// Requires <c>roles.manage</c>.
    /// </summary>
    [HttpPost("{tenantRef:guid}/members/{userRef:guid}/roles/{roleRef:guid}")]
    public async Task<IActionResult> AssignUserRole(Guid tenantRef, Guid userRef, Guid roleRef, [FromBody] AssignmentScopeRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: AssignUserRole requested (UserRef: {UserRef}, RoleRef: {RoleRef}, WorkspaceRef: {WorkspaceRef})", userRef, roleRef, request.WorkspaceRef);
        return await WithCallerAsync(callerId => _managementService.AssignUserRoleAsync(callerId, tenantRef, userRef, roleRef, request.WorkspaceRef, cancellationToken));
    }

    /// <summary>Removes a role assignment from a user. Requires <c>roles.manage</c>.</summary>
    [HttpDelete("{tenantRef:guid}/members/{userRef:guid}/roles/{roleRef:guid}")]
    public async Task<IActionResult> RemoveUserRole(Guid tenantRef, Guid userRef, Guid roleRef, [FromQuery] Guid? workspaceRef, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: RemoveUserRole requested (UserRef: {UserRef}, RoleRef: {RoleRef}, WorkspaceRef: {WorkspaceRef})", userRef, roleRef, workspaceRef);
        return await WithCallerAsync(callerId => _managementService.RemoveUserRoleAsync(callerId, tenantRef, userRef, roleRef, workspaceRef, cancellationToken));
    }

    /// <summary>Grants or denies a permission directly to a user. Requires <c>roles.manage</c>.</summary>
    [HttpPost("{tenantRef:guid}/members/{userRef:guid}/permissions")]
    public async Task<IActionResult> GrantUserPermission(Guid tenantRef, Guid userRef, [FromBody] GrantPermissionRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: GrantUserPermission requested (UserRef: {UserRef}, Slug: '{Slug}', IsDeny: {IsDeny})", userRef, request.Slug, request.IsDeny);
        return await WithCallerAsync(callerId => _managementService.GrantUserPermissionAsync(callerId, tenantRef, userRef, request, cancellationToken));
    }

    /// <summary>
    /// Removes a direct user permission, returning the decision to the user's roles. This is not the
    /// same as denying it — a user-level row wins over any role, so without this an accidental grant
    /// could never be undone. Requires <c>roles.manage</c>.
    /// </summary>
    [HttpDelete("{tenantRef:guid}/members/{userRef:guid}/permissions/{slug}")]
    public async Task<IActionResult> RemoveUserPermission(Guid tenantRef, Guid userRef, string slug, [FromQuery] Guid? workspaceRef, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: RemoveUserPermission requested (UserRef: {UserRef}, Slug: '{Slug}', WorkspaceRef: {WorkspaceRef})", userRef, slug, workspaceRef);
        return await WithCallerAsync(callerId => _managementService.RemoveUserPermissionAsync(callerId, tenantRef, userRef, slug, workspaceRef, cancellationToken));
    }

    /// <summary>
    /// Enables or disables an account. Disabling revokes every refresh token the user holds; an
    /// access token already issued stays valid until it expires. Requires <c>users.manage</c>.
    /// </summary>
    [HttpPut("{tenantRef:guid}/members/{userRef:guid}/active")]
    public async Task<IActionResult> SetUserActive(Guid tenantRef, Guid userRef, [FromBody] SetUserActiveRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("API: SetUserActive requested (UserRef: {UserRef}, IsActive: {IsActive})", userRef, request.IsActive);

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return await WithCallerAsync(callerId => _managementService.SetUserActiveAsync(callerId, tenantRef, userRef, request.IsActive, ipAddress, cancellationToken));
    }

    // ----- Shared plumbing ---------------------------------------------------------------------

    private async Task<IActionResult> WithCallerAsync(Func<int, Task<Result>> operation)
    {
        var caller = CurrentUserId();
        if (caller.IsFailure)
        {
            return MapError(caller.Error);
        }

        return MapResult(await operation(caller.Value));
    }

    private async Task<ActionResult<ListResponse<T>>> PagedAsync<T>(Guid tenantRef, int skip, int take, Func<int, int, int, Task<Result<IPagedList<T>>>> operation)
    {
        var caller = CurrentUserId();
        if (caller.IsFailure)
        {
            return MapError(caller.Error);
        }

        // Clamped rather than rejected: an out-of-range page size is not worth failing a read over,
        // but an unbounded one would let a caller pull the whole table in a single request.
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, MaxPageSize);

        var result = await operation(caller.Value, skip, take);
        if (result.IsFailure)
        {
            return MapError(result.Error);
        }

        // The total is what makes the skip/take pair usable — without it a caller cannot tell a
        // final page from a full one. Matches the header the management host's list endpoints set.
        Response.Headers.Append("X-Total-Count", result.Value.TotalCount.ToString());

        return Ok(new ListResponse<T> { Items = result.Value });
    }
}

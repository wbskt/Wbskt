using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wbskt.Auth.Host.Authorization;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Models;
using Wbskt.Auth.Host.Services;
using Wbskt.Infrastructure;
using Wbskt.Models;
using Wbskt.Primitives.Constants;

namespace Wbskt.Auth.Host.Controllers;

/// <summary>
/// Tenant administration. Everything is addressed by public <c>Guid</c> reference and scoped to a
/// tenant in the route, matching the workspace-scoped controllers in the management host. Each
/// tenant action declares what it needs with <see cref="RequiresTenantPermissionAttribute"/>;
/// <see cref="TenantPermissionFilter"/> resolves the tenant and checks the caller before the action
/// runs, and hands it the tenant's id as <c>[FromTenant] int tenantId</c>.
/// </summary>
[Route("api/tenants")]
[AnnouncesWorkspaceAccessChange]
[ApiController]
[Authorize]
public class ManagementController(ITenantService tenantService, IAccessControlService accessControlService) : ApiControllerBase
{
    private const int DefaultPageSize = 100;

    /// <summary>
    /// Retrieves the tenants the current user belongs to. This is the entry point for every other
    /// endpoint here, since they are all addressed by tenant reference.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<TenantResponse>>> GetTenants(CancellationToken cancellationToken)
    {
        var caller = CurrentUserId();
        return caller.IsFailure ? MapError(caller.Error) : MapResult(await tenantService.GetTenantsForUserAsync(caller.Value, cancellationToken));
    }

    /// <summary>
    /// Creates a tenant with the caller as its administrator, along with a default workspace.
    /// Requires no permission — permissions are held inside a tenant, so demanding one to create the
    /// first would be circular.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TenantResponse>> CreateTenant([FromBody] CreateTenantRequest request, CancellationToken cancellationToken)
    {
        var caller = CurrentUserId();
        return caller.IsFailure ? MapError(caller.Error) : MapResult(await tenantService.CreateTenantAsync(caller.Value, request, cancellationToken));
    }

    /// <summary>
    /// Renames a tenant or changes its description. This is how the tenant a user is given at
    /// sign-up becomes a named organisation.
    /// </summary>
    [HttpPut("{tenantRef:guid}")]
    [RequiresTenantPermission(PermissionNames.UsersManage)]
    public async Task<IActionResult> UpdateTenant([FromTenant] int tenantId, [FromBody] UpdateTenantRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await tenantService.UpdateTenantAsync(tenantId, request, cancellationToken));
    }

    // ----- Invitations -------------------------------------------------------------------------

    /// <summary>Lists invitations that have neither been accepted nor revoked.</summary>
    [HttpGet("{tenantRef:guid}/invitations")]
    [RequiresTenantPermission(PermissionNames.UsersRead)]
    public async Task<ActionResult<Page<InvitationResponse>>> GetInvitations([FromTenant] int tenantId, [FromQuery] PageRequest page, CancellationToken cancellationToken)
    {
        return await PagedAsync(page, (s, t) => tenantService.GetInvitationsAsync(tenantId, s, t, cancellationToken));
    }

    /// <summary>
    /// Invites an address to the tenant, optionally with a role granted on acceptance. The response
    /// carries the raw token once; it is stored only as a hash and cannot be retrieved again.
    /// </summary>
    [HttpPost("{tenantRef:guid}/invitations")]
    [RequiresTenantPermission(PermissionNames.UsersManage)]
    public async Task<ActionResult<CreatedInvitationResponse>> CreateInvitation([FromTenant] int tenantId, [FromBody] CreateInvitationRequest request, CancellationToken cancellationToken)
    {
        var caller = CurrentUserId();
        return caller.IsFailure ? MapError(caller.Error) : MapResult(await tenantService.CreateInvitationAsync(caller.Value, tenantId, request, cancellationToken));
    }

    /// <summary>Withdraws an outstanding invitation.</summary>
    [HttpDelete("{tenantRef:guid}/invitations/{invitationRef:guid}")]
    [RequiresTenantPermission(PermissionNames.UsersManage)]
    public async Task<IActionResult> RevokeInvitation([FromTenant] int tenantId, Guid invitationRef, CancellationToken cancellationToken)
    {
        return MapResult(await tenantService.RevokeInvitationAsync(tenantId, invitationRef, cancellationToken));
    }

    // ----- Roles -------------------------------------------------------------------------------

    /// <summary>Lists the roles defined in a tenant.</summary>
    [HttpGet("{tenantRef:guid}/roles")]
    [RequiresTenantPermission(PermissionNames.RolesRead)]
    public async Task<ActionResult<Page<RoleResponse>>> GetRoles([FromTenant] int tenantId, [FromQuery] PageRequest page, CancellationToken cancellationToken)
    {
        return await PagedAsync(page, (s, t) => accessControlService.GetRolesAsync(tenantId, s, t, cancellationToken));
    }

    /// <summary>Creates a role.</summary>
    [HttpPost("{tenantRef:guid}/roles")]
    [RequiresTenantPermission(PermissionNames.RolesManage)]
    public async Task<ActionResult<RoleResponse>> CreateRole([FromTenant] int tenantId, [FromBody] CreateRoleRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await accessControlService.CreateRoleAsync(tenantId, request, cancellationToken));
    }

    /// <summary>Renames a role or changes its description.</summary>
    [HttpPut("{tenantRef:guid}/roles/{roleRef:guid}")]
    [RequiresTenantPermission(PermissionNames.RolesManage)]
    public async Task<IActionResult> UpdateRole([FromTenant] int tenantId, Guid roleRef, [FromBody] UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await accessControlService.UpdateRoleAsync(tenantId, roleRef, request, cancellationToken));
    }

    /// <summary>Deletes a role along with its permissions and every assignment of it.</summary>
    [HttpDelete("{tenantRef:guid}/roles/{roleRef:guid}")]
    [RequiresTenantPermission(PermissionNames.RolesManage)]
    public async Task<IActionResult> DeleteRole([FromTenant] int tenantId, Guid roleRef, CancellationToken cancellationToken)
    {
        return MapResult(await accessControlService.DeleteRoleAsync(tenantId, roleRef, cancellationToken));
    }

    /// <summary>Lists the permissions attached to a role.</summary>
    [HttpGet("{tenantRef:guid}/roles/{roleRef:guid}/permissions")]
    [RequiresTenantPermission(PermissionNames.RolesRead)]
    public async Task<ActionResult<IReadOnlyCollection<RolePermissionAssignmentResponse>>> GetRolePermissions([FromTenant] int tenantId, Guid roleRef, CancellationToken cancellationToken)
    {
        return MapResult(await accessControlService.GetRolePermissionsAsync(tenantId, roleRef, cancellationToken));
    }

    /// <summary>
    /// Grants or denies a permission on a role. Role permissions are unscoped — the workspace is
    /// chosen when the role is assigned, so this body takes no <c>workspaceRef</c>.
    /// </summary>
    [HttpPost("{tenantRef:guid}/roles/{roleRef:guid}/permissions")]
    [RequiresTenantPermission(PermissionNames.RolesManage)]
    public async Task<IActionResult> GrantRolePermission([FromTenant] int tenantId, Guid roleRef, [FromBody] GrantRolePermissionRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await accessControlService.GrantRolePermissionAsync(tenantId, roleRef, request, cancellationToken));
    }

    /// <summary>Detaches a permission from a role, as distinct from denying it.</summary>
    [HttpDelete("{tenantRef:guid}/roles/{roleRef:guid}/permissions/{slug}")]
    [RequiresTenantPermission(PermissionNames.RolesManage)]
    public async Task<IActionResult> RemoveRolePermission([FromTenant] int tenantId, Guid roleRef, string slug, CancellationToken cancellationToken)
    {
        return MapResult(await accessControlService.RemoveRolePermissionAsync(tenantId, roleRef, slug, cancellationToken));
    }

    // ----- Groups ------------------------------------------------------------------------------

    /// <summary>Lists the groups defined in a tenant.</summary>
    [HttpGet("{tenantRef:guid}/groups")]
    [RequiresTenantPermission(PermissionNames.UsersRead)]
    public async Task<ActionResult<Page<GroupResponse>>> GetGroups([FromTenant] int tenantId, [FromQuery] PageRequest page, CancellationToken cancellationToken)
    {
        return await PagedAsync(page, (s, t) => accessControlService.GetGroupsAsync(tenantId, s, t, cancellationToken));
    }

    /// <summary>Creates a group, optionally nested under a parent.</summary>
    [HttpPost("{tenantRef:guid}/groups")]
    [RequiresTenantPermission(PermissionNames.UsersManage)]
    public async Task<ActionResult<GroupResponse>> CreateGroup([FromTenant] int tenantId, [FromBody] CreateGroupRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await accessControlService.CreateGroupAsync(tenantId, request, cancellationToken));
    }

    /// <summary>Renames a group.</summary>
    [HttpPut("{tenantRef:guid}/groups/{groupRef:guid}")]
    [RequiresTenantPermission(PermissionNames.UsersManage)]
    public async Task<IActionResult> UpdateGroup([FromTenant] int tenantId, Guid groupRef, [FromBody] UpdateGroupRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await accessControlService.UpdateGroupAsync(tenantId, groupRef, request, cancellationToken));
    }

    /// <summary>Deletes a group. Rejected if it still has child groups.</summary>
    [HttpDelete("{tenantRef:guid}/groups/{groupRef:guid}")]
    [RequiresTenantPermission(PermissionNames.UsersManage)]
    public async Task<IActionResult> DeleteGroup([FromTenant] int tenantId, Guid groupRef, CancellationToken cancellationToken)
    {
        return MapResult(await accessControlService.DeleteGroupAsync(tenantId, groupRef, cancellationToken));
    }

    /// <summary>Lists the roles assigned to a group.</summary>
    [HttpGet("{tenantRef:guid}/groups/{groupRef:guid}/roles")]
    [RequiresTenantPermission(PermissionNames.RolesRead)]
    public async Task<ActionResult<IReadOnlyCollection<RoleAssignmentResponse>>> GetGroupRoles([FromTenant] int tenantId, Guid groupRef, CancellationToken cancellationToken)
    {
        return MapResult(await accessControlService.GetGroupRolesAsync(tenantId, groupRef, cancellationToken));
    }

    /// <summary>Assigns a role to a group. A null <c>workspaceRef</c> in the body means tenant-wide.</summary>
    [HttpPost("{tenantRef:guid}/groups/{groupRef:guid}/roles/{roleRef:guid}")]
    [RequiresTenantPermission(PermissionNames.RolesManage)]
    public async Task<IActionResult> AssignGroupRole([FromTenant] int tenantId, Guid groupRef, Guid roleRef, [FromBody] AssignmentScopeRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await accessControlService.AssignGroupRoleAsync(tenantId, groupRef, roleRef, request.WorkspaceRef, cancellationToken));
    }

    /// <summary>Removes a role assignment from a group.</summary>
    [HttpDelete("{tenantRef:guid}/groups/{groupRef:guid}/roles/{roleRef:guid}")]
    [RequiresTenantPermission(PermissionNames.RolesManage)]
    public async Task<IActionResult> RemoveGroupRole([FromTenant] int tenantId, Guid groupRef, Guid roleRef, [FromQuery] Guid? workspaceRef, CancellationToken cancellationToken)
    {
        return MapResult(await accessControlService.RemoveGroupRoleAsync(tenantId, groupRef, roleRef, workspaceRef, cancellationToken));
    }

    // ----- Permissions catalogue ---------------------------------------------------------------

    /// <summary>
    /// Lists the permission catalogue. The catalogue is global and code-defined, so the tenant here
    /// only scopes the permission check.
    /// </summary>
    [HttpGet("{tenantRef:guid}/permissions")]
    [RequiresTenantPermission(PermissionNames.RolesRead)]
    public async Task<ActionResult<Page<PermissionResponse>>> GetPermissions([FromTenant] int tenantId, [FromQuery] PageRequest page, CancellationToken cancellationToken)
    {
        return await PagedAsync(page, (s, t) => accessControlService.GetPermissionsAsync(tenantId, s, t, cancellationToken));
    }

    // ----- Members -----------------------------------------------------------------------------

    /// <summary>
    /// Lists the users in a tenant. This is how a caller discovers the user references every other
    /// endpoint here needs.
    /// </summary>
    [HttpGet("{tenantRef:guid}/members")]
    [RequiresTenantPermission(PermissionNames.UsersRead)]
    public async Task<ActionResult<Page<TenantMemberResponse>>> GetMembers([FromTenant] int tenantId, [FromQuery] string? search, [FromQuery] PageRequest page, CancellationToken cancellationToken)
    {
        return await PagedAsync(page, (s, t) => tenantService.GetMembersAsync(tenantId, search, s, t, cancellationToken));
    }

    /// <summary>Lists the roles a user holds and the scope of each.</summary>
    [HttpGet("{tenantRef:guid}/members/{userRef:guid}/roles")]
    [RequiresTenantPermission(PermissionNames.RolesRead)]
    public async Task<ActionResult<IReadOnlyCollection<RoleAssignmentResponse>>> GetUserRoles([FromTenant] int tenantId, Guid userRef, CancellationToken cancellationToken)
    {
        return MapResult(await accessControlService.GetUserRolesAsync(tenantId, userRef, cancellationToken));
    }

    /// <summary>
    /// Lists the permissions granted directly to a user. These override role-derived permissions in
    /// both directions, so this is where an unexpected allow or deny is diagnosed.
    /// </summary>
    [HttpGet("{tenantRef:guid}/members/{userRef:guid}/permissions")]
    [RequiresTenantPermission(PermissionNames.RolesRead)]
    public async Task<ActionResult<IReadOnlyCollection<UserPermissionAssignmentResponse>>> GetUserPermissions([FromTenant] int tenantId, Guid userRef, CancellationToken cancellationToken)
    {
        return MapResult(await accessControlService.GetUserPermissionsAsync(tenantId, userRef, cancellationToken));
    }

    /// <summary>Lists the groups a user belongs to.</summary>
    [HttpGet("{tenantRef:guid}/members/{userRef:guid}/groups")]
    [RequiresTenantPermission(PermissionNames.UsersRead)]
    public async Task<ActionResult<IReadOnlyCollection<GroupMembershipResponse>>> GetUserGroups([FromTenant] int tenantId, Guid userRef, CancellationToken cancellationToken)
    {
        return MapResult(await accessControlService.GetUserGroupsAsync(tenantId, userRef, cancellationToken));
    }

    /// <summary>Adds a user to a group.</summary>
    [HttpPost("{tenantRef:guid}/members/{userRef:guid}/groups/{groupRef:guid}")]
    [RequiresTenantPermission(PermissionNames.UsersManage)]
    public async Task<IActionResult> AddUserToGroup([FromTenant] int tenantId, Guid userRef, Guid groupRef, CancellationToken cancellationToken)
    {
        return MapResult(await accessControlService.AddUserToGroupAsync(tenantId, userRef, groupRef, cancellationToken));
    }

    /// <summary>Removes a user from a group.</summary>
    [HttpDelete("{tenantRef:guid}/members/{userRef:guid}/groups/{groupRef:guid}")]
    [RequiresTenantPermission(PermissionNames.UsersManage)]
    public async Task<IActionResult> RemoveUserFromGroup([FromTenant] int tenantId, Guid userRef, Guid groupRef, CancellationToken cancellationToken)
    {
        return MapResult(await accessControlService.RemoveUserFromGroupAsync(tenantId, userRef, groupRef, cancellationToken));
    }

    /// <summary>Assigns a role to a user. A null <c>workspaceRef</c> in the body means tenant-wide.</summary>
    [HttpPost("{tenantRef:guid}/members/{userRef:guid}/roles/{roleRef:guid}")]
    [RequiresTenantPermission(PermissionNames.RolesManage)]
    public async Task<IActionResult> AssignUserRole([FromTenant] int tenantId, Guid userRef, Guid roleRef, [FromBody] AssignmentScopeRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await accessControlService.AssignUserRoleAsync(tenantId, userRef, roleRef, request.WorkspaceRef, cancellationToken));
    }

    /// <summary>Removes a role assignment from a user.</summary>
    [HttpDelete("{tenantRef:guid}/members/{userRef:guid}/roles/{roleRef:guid}")]
    [RequiresTenantPermission(PermissionNames.RolesManage)]
    public async Task<IActionResult> RemoveUserRole([FromTenant] int tenantId, Guid userRef, Guid roleRef, [FromQuery] Guid? workspaceRef, CancellationToken cancellationToken)
    {
        return MapResult(await accessControlService.RemoveUserRoleAsync(tenantId, userRef, roleRef, workspaceRef, cancellationToken));
    }

    /// <summary>Grants or denies a permission directly to a user.</summary>
    [HttpPost("{tenantRef:guid}/members/{userRef:guid}/permissions")]
    [RequiresTenantPermission(PermissionNames.RolesManage)]
    public async Task<IActionResult> GrantUserPermission([FromTenant] int tenantId, Guid userRef, [FromBody] GrantPermissionRequest request, CancellationToken cancellationToken)
    {
        return MapResult(await accessControlService.GrantUserPermissionAsync(tenantId, userRef, request, cancellationToken));
    }

    /// <summary>
    /// Removes a direct user permission, returning the decision to the user's roles. This is not the
    /// same as denying it — a user-level row wins over any role, so without this an accidental grant
    /// could never be undone.
    /// </summary>
    [HttpDelete("{tenantRef:guid}/members/{userRef:guid}/permissions/{slug}")]
    [RequiresTenantPermission(PermissionNames.RolesManage)]
    public async Task<IActionResult> RemoveUserPermission([FromTenant] int tenantId, Guid userRef, string slug, [FromQuery] Guid? workspaceRef, CancellationToken cancellationToken)
    {
        return MapResult(await accessControlService.RemoveUserPermissionAsync(tenantId, userRef, slug, workspaceRef, cancellationToken));
    }

    /// <summary>
    /// Removes a member from the tenant, along with every assignment scoped to it, and transfers any
    /// workspace they owned to the caller. Their account and their other tenants are untouched —
    /// this is offboarding from one tenant, not account closure.
    /// </summary>
    [HttpDelete("{tenantRef:guid}/members/{userRef:guid}")]
    [RequiresTenantPermission(PermissionNames.UsersManage)]
    public async Task<IActionResult> RemoveMember([FromTenant] int tenantId, Guid userRef, CancellationToken cancellationToken)
    {
        var caller = CurrentUserId();
        return caller.IsFailure ? MapError(caller.Error) : MapResult(await tenantService.RemoveTenantMemberAsync(caller.Value, tenantId, userRef, cancellationToken));
    }

    /// <summary>
    /// Suspends a member in this tenant, or lifts the suspension. A suspended member keeps their
    /// account, their other tenants and everything assigned to them here, but holds no permission in
    /// this tenant and cannot see or enter its workspaces until it is lifted. Refused for yourself and
    /// for the tenant's last administrator.
    /// <para>
    /// Disabling an account outright is deliberately not available to tenant administrators: it
    /// would lock the person out of every tenant they belong to, including ones the caller has no
    /// say over. To remove someone from this tenant, use <see cref="RemoveMember"/>.
    /// </para>
    /// </summary>
    [HttpPut("{tenantRef:guid}/members/{userRef:guid}/suspended")]
    [RequiresTenantPermission(PermissionNames.UsersManage)]
    public async Task<IActionResult> SetMemberSuspended([FromTenant] int tenantId, Guid userRef, [FromBody] SetMemberSuspendedRequest request, CancellationToken cancellationToken)
    {
        var caller = CurrentUserId();
        if (caller.IsFailure)
        {
            return MapError(caller.Error);
        }

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return MapResult(await tenantService.SetMemberSuspendedAsync(caller.Value, tenantId, userRef, request.IsSuspended, ipAddress, cancellationToken));
    }

    // ----- Shared plumbing ---------------------------------------------------------------------

    private async Task<ActionResult<Page<T>>> PagedAsync<T>(PageRequest page, Func<int, int, Task<Result<IPagedList<T>>>> operation)
    {
        var offset = page.Offset();
        if (offset.IsFailure)
        {
            return MapError(offset.Error);
        }

        return MapPage(await operation(offset.Value, page.LimitOr(DefaultPageSize)), offset.Value);
    }
}

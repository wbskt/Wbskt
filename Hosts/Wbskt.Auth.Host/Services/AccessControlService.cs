using Wbskt.Auth.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Models;

namespace Wbskt.Auth.Host.Services;

internal sealed class AccessControlService : IAccessControlService
{
    private readonly IAccessControlProvider _accessControl;
    private readonly ITenantProvider _tenants;
    private readonly IWorkspaceProvider _workspaceProvider;
    private readonly IEventBus _eventBus;
    private readonly ILogger<AccessControlService> _logger;
    private readonly TenantAnnouncer _announcer;

    public AccessControlService(
        IAccessControlProvider accessControl,
        ITenantProvider tenants,
        IAccountProvider accounts,
        IWorkspaceProvider workspaceProvider,
        IEventBus eventBus,
        ILogger<AccessControlService> logger,
        IAuditWorkspaces? audit = null)
    {
        _accessControl = accessControl;
        _tenants = tenants;
        _workspaceProvider = workspaceProvider;
        _eventBus = eventBus;
        _logger = logger;
        _announcer = new TenantAnnouncer(accounts, eventBus, audit ?? AuditWorkspaces.None, logger);
    }

    // ----- Roles -------------------------------------------------------------------------------

    public async Task<Result<IPagedList<RoleResponse>>> GetRolesAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await GuardAsync("GetRoles", () => _accessControl.GetRolesAsync(tenantId, skip, take, cancellationToken));
    }

    public async Task<Result<RoleResponse>> CreateRoleAsync(int tenantId, CreateRoleRequest request, CancellationToken cancellationToken = default)
    {
        return await GuardAsync("CreateRole", async () =>
        {
            var refId = await _accessControl.InsertRoleAsync(request.Name, request.Description, tenantId, cancellationToken);
            _logger.LogInformation("Role '{RoleName}' created in tenant {TenantId} with RefId {RefId}", request.Name, tenantId, refId);
            return new RoleResponse(refId, request.Name, request.Description);
        }, Error.Conflict("AUTH_ROLE_CONFLICT", "A role with that name already exists in this tenant."));
    }

    public async Task<Result> UpdateRoleAsync(int tenantId, Guid roleRef, UpdateRoleRequest request, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveRoleAsync(tenantId, roleRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        return await GuardAsync("UpdateRole", () => _accessControl.UpdateRoleAsync(resolved.Value.EntityId, resolved.Value.TenantId, request.Name, request.Description, cancellationToken),
            Error.Conflict("AUTH_ROLE_CONFLICT", "A role with that name already exists in this tenant."));
    }

    public async Task<Result> DeleteRoleAsync(int tenantId, Guid roleRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveRoleAsync(tenantId, roleRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        return await GuardAsync("DeleteRole", async () =>
        {
            await _accessControl.DeleteRoleAsync(resolved.Value.EntityId, resolved.Value.TenantId, cancellationToken);

            // The role's permissions are gone, so anyone who held it has a different effective set now.
            await _eventBus.PublishAsync(new RolePermissionsChangedEvent(resolved.Value.EntityId), cancellationToken);
        });
    }

    // ----- Groups ------------------------------------------------------------------------------

    public async Task<Result<IPagedList<GroupResponse>>> GetGroupsAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await GuardAsync("GetGroups", () => _accessControl.GetGroupsAsync(tenantId, skip, take, cancellationToken));
    }

    public async Task<Result<GroupResponse>> CreateGroupAsync(int tenantId, CreateGroupRequest request, CancellationToken cancellationToken = default)
    {
        int? parentGroupId = null;
        if (request.ParentGroupRef.HasValue)
        {
            var parentId = await _accessControl.FindGroupIdByRefIdAsync(request.ParentGroupRef.Value, tenantId, cancellationToken);
            if (parentId <= 0)
            {
                return Result<GroupResponse>.Failure(Error.Forbidden("GROUP_NOT_FOUND", "Parent group not found in this tenant."));
            }

            parentGroupId = parentId;
        }

        return await GuardAsync("CreateGroup", async () =>
        {
            var refId = await _accessControl.InsertGroupAsync(request.Name, parentGroupId, tenantId, cancellationToken);
            _logger.LogInformation("Group '{GroupName}' created in tenant {TenantId} with RefId {RefId}", request.Name, tenantId, refId);
            return new GroupResponse(refId, request.Name, request.ParentGroupRef);
        }, Error.Conflict("AUTH_GROUP_CONFLICT", "A group with that name already exists in this tenant."));
    }

    public async Task<Result> UpdateGroupAsync(int tenantId, Guid groupRef, UpdateGroupRequest request, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveGroupAsync(tenantId, groupRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        return await GuardAsync("UpdateGroup", () => _accessControl.UpdateGroupAsync(resolved.Value.EntityId, resolved.Value.TenantId, request.Name, cancellationToken),
            Error.Conflict("AUTH_GROUP_CONFLICT", "A group with that name already exists in this tenant."));
    }

    public async Task<Result> DeleteGroupAsync(int tenantId, Guid groupRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveGroupAsync(tenantId, groupRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        return await GuardAsync("DeleteGroup", () => _accessControl.DeleteGroupAsync(resolved.Value.EntityId, resolved.Value.TenantId, cancellationToken));
    }

    // ----- Permission catalogue ----------------------------------------------------------------

    public async Task<Result<IPagedList<PermissionResponse>>> GetPermissionsAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default)
    {
        // The catalogue itself is global; the tenant only scopes the permission check.
        return await GuardAsync("GetPermissions", () => _accessControl.GetPermissionsAsync(skip, take, cancellationToken));
    }

    // ----- Group membership --------------------------------------------------------------------

    public async Task<Result> AddUserToGroupAsync(int tenantId, Guid userRef, Guid groupRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAndGroupAsync(tenantId, userRef, groupRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        var (_, userId, groupId) = resolved.Value;
        return await GuardAsync("AddUserToGroup", async () =>
        {
            await _accessControl.InsertUserGroupAsync(userId, groupId, tenantId, cancellationToken);
            await _announcer.UserPermissionsChangedAsync(userId, cancellationToken);
        });
    }

    public async Task<Result> RemoveUserFromGroupAsync(int tenantId, Guid userRef, Guid groupRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAndGroupAsync(tenantId, userRef, groupRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        var (_, userId, groupId) = resolved.Value;
        return await GuardAsync("RemoveUserFromGroup", async () =>
        {
            await _accessControl.RemoveUserGroupAsync(userId, groupId, tenantId, cancellationToken);
            await _announcer.UserPermissionsChangedAsync(userId, cancellationToken);
        });
    }

    // ----- Permission assignments --------------------------------------------------------------

    public async Task<Result> GrantRolePermissionAsync(int tenantId, Guid roleRef, GrantRolePermissionRequest request, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveRoleAsync(tenantId, roleRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        return await GuardAsync("GrantRolePermission", () => ChangeRolePermissionsAsync(roleRef, resolved.Value, () =>
            _accessControl.GrantRolePermissionAsync(resolved.Value.EntityId, request.Slug, request.IsDeny, resolved.Value.TenantId, cancellationToken), cancellationToken));
    }

    public async Task<Result> RemoveRolePermissionAsync(int tenantId, Guid roleRef, string slug, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveRoleAsync(tenantId, roleRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        return await GuardAsync("RemoveRolePermission", () => ChangeRolePermissionsAsync(roleRef, resolved.Value, () =>
            _accessControl.RemoveRolePermissionAsync(resolved.Value.EntityId, slug, resolved.Value.TenantId, cancellationToken), cancellationToken));
    }

    public async Task<Result> GrantUserPermissionAsync(int tenantId, Guid userRef, GrantPermissionRequest request, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAsync(tenantId, userRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        var workspace = await ResolveWorkspaceScopeAsync(request.WorkspaceRef, cancellationToken);
        if (workspace.IsFailure)
        {
            return Result.Failure(workspace.Error);
        }

        return await GuardAsync("GrantUserPermission", async () =>
        {
            await _accessControl.GrantUserPermissionAsync(resolved.Value.EntityId, request.Slug, request.IsDeny, resolved.Value.TenantId, workspace.Value, cancellationToken);
            await _announcer.UserPermissionsChangedAsync(resolved.Value.EntityId, cancellationToken);
        });
    }

    public async Task<Result> RemoveUserPermissionAsync(int tenantId, Guid userRef, string slug, Guid? workspaceRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAsync(tenantId, userRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        var workspace = await ResolveWorkspaceScopeAsync(workspaceRef, cancellationToken);
        if (workspace.IsFailure)
        {
            return Result.Failure(workspace.Error);
        }

        return await GuardAsync("RemoveUserPermission", async () =>
        {
            await _accessControl.RemoveUserPermissionAsync(resolved.Value.EntityId, slug, resolved.Value.TenantId, workspace.Value, cancellationToken);
            await _announcer.UserPermissionsChangedAsync(resolved.Value.EntityId, cancellationToken);
        });
    }

    // ----- Role assignments --------------------------------------------------------------------

    public Task<Result> AssignUserRoleAsync(int tenantId, Guid userRef, Guid roleRef, Guid? workspaceRef, CancellationToken cancellationToken = default)
        => ChangeUserRoleAsync(tenantId, userRef, roleRef, workspaceRef, assign: true, cancellationToken);

    public Task<Result> RemoveUserRoleAsync(int tenantId, Guid userRef, Guid roleRef, Guid? workspaceRef, CancellationToken cancellationToken = default)
        => ChangeUserRoleAsync(tenantId, userRef, roleRef, workspaceRef, assign: false, cancellationToken);

    public Task<Result> AssignGroupRoleAsync(int tenantId, Guid groupRef, Guid roleRef, Guid? workspaceRef, CancellationToken cancellationToken = default)
        => ChangeGroupRoleAsync(tenantId, groupRef, roleRef, workspaceRef, assign: true, cancellationToken);

    public Task<Result> RemoveGroupRoleAsync(int tenantId, Guid groupRef, Guid roleRef, Guid? workspaceRef, CancellationToken cancellationToken = default)
        => ChangeGroupRoleAsync(tenantId, groupRef, roleRef, workspaceRef, assign: false, cancellationToken);

    private async Task<Result> ChangeUserRoleAsync(int tenantId, Guid userRef, Guid roleRef, Guid? workspaceRef, bool assign, CancellationToken cancellationToken)
    {
        var userId = await _tenants.FindUserIdByRefIdInTenantAsync(userRef, tenantId, cancellationToken);
        if (userId <= 0)
        {
            return Result.Failure(Error.Forbidden("USER_NOT_FOUND", "User not found in this tenant."));
        }

        var roleId = await _accessControl.FindRoleIdByRefIdAsync(roleRef, tenantId, cancellationToken);
        if (roleId <= 0)
        {
            return Result.Failure(Error.Forbidden("ROLE_NOT_FOUND", "Role not found in this tenant."));
        }

        var workspace = await ResolveWorkspaceScopeAsync(workspaceRef, cancellationToken);
        if (workspace.IsFailure)
        {
            return Result.Failure(workspace.Error);
        }

        return await GuardAsync(assign ? "AssignUserRole" : "RemoveUserRole", async () =>
        {
            if (assign)
            {
                await _accessControl.AssignUserRoleAsync(userId, roleId, tenantId, workspace.Value, cancellationToken);
            }
            else
            {
                await _accessControl.RemoveUserRoleAsync(userId, roleId, tenantId, workspace.Value, cancellationToken);
            }

            await _announcer.UserPermissionsChangedAsync(userId, cancellationToken);
            await _announcer.ToTenantAsync(
                assign ? new MemberRoleAssignedEvent(userRef, roleRef, workspaceRef) : new MemberRoleRemovedEvent(userRef, roleRef, workspaceRef),
                tenantId, cancellationToken);
        });
    }

    private async Task<Result> ChangeGroupRoleAsync(int tenantId, Guid groupRef, Guid roleRef, Guid? workspaceRef, bool assign, CancellationToken cancellationToken)
    {
        var groupId = await _accessControl.FindGroupIdByRefIdAsync(groupRef, tenantId, cancellationToken);
        if (groupId <= 0)
        {
            return Result.Failure(Error.Forbidden("GROUP_NOT_FOUND", "Group not found in this tenant."));
        }

        var roleId = await _accessControl.FindRoleIdByRefIdAsync(roleRef, tenantId, cancellationToken);
        if (roleId <= 0)
        {
            return Result.Failure(Error.Forbidden("ROLE_NOT_FOUND", "Role not found in this tenant."));
        }

        var workspace = await ResolveWorkspaceScopeAsync(workspaceRef, cancellationToken);
        if (workspace.IsFailure)
        {
            return Result.Failure(workspace.Error);
        }

        return await GuardAsync(assign ? "AssignGroupRole" : "RemoveGroupRole", async () =>
        {
            if (assign)
            {
                await _accessControl.AssignGroupRoleAsync(groupId, roleId, tenantId, workspace.Value, cancellationToken);
            }
            else
            {
                await _accessControl.RemoveGroupRoleAsync(groupId, roleId, tenantId, workspace.Value, cancellationToken);
            }

            await _eventBus.PublishAsync(new RolePermissionsChangedEvent(roleId), cancellationToken);
        });
    }

    // ----- Assignment read-back ----------------------------------------------------------------

    public async Task<Result<IReadOnlyCollection<RoleAssignmentResponse>>> GetUserRolesAsync(int tenantId, Guid userRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAsync(tenantId, userRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result<IReadOnlyCollection<RoleAssignmentResponse>>.Failure(resolved.Error);
        }

        return await GuardAsync("GetUserRoles", () => _accessControl.GetUserRolesAsync(resolved.Value.EntityId, resolved.Value.TenantId, cancellationToken));
    }

    public async Task<Result<IReadOnlyCollection<UserPermissionAssignmentResponse>>> GetUserPermissionsAsync(int tenantId, Guid userRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAsync(tenantId, userRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result<IReadOnlyCollection<UserPermissionAssignmentResponse>>.Failure(resolved.Error);
        }

        return await GuardAsync("GetUserPermissions", () => _accessControl.GetUserPermissionsAsync(resolved.Value.EntityId, resolved.Value.TenantId, cancellationToken));
    }

    public async Task<Result<IReadOnlyCollection<GroupMembershipResponse>>> GetUserGroupsAsync(int tenantId, Guid userRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAsync(tenantId, userRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result<IReadOnlyCollection<GroupMembershipResponse>>.Failure(resolved.Error);
        }

        return await GuardAsync("GetUserGroups", () => _accessControl.GetUserGroupsAsync(resolved.Value.EntityId, resolved.Value.TenantId, cancellationToken));
    }

    public async Task<Result<IReadOnlyCollection<RoleAssignmentResponse>>> GetGroupRolesAsync(int tenantId, Guid groupRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveGroupAsync(tenantId, groupRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result<IReadOnlyCollection<RoleAssignmentResponse>>.Failure(resolved.Error);
        }

        return await GuardAsync("GetGroupRoles", () => _accessControl.GetGroupRolesAsync(resolved.Value.EntityId, resolved.Value.TenantId, cancellationToken));
    }

    public async Task<Result<IReadOnlyCollection<RolePermissionAssignmentResponse>>> GetRolePermissionsAsync(int tenantId, Guid roleRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveRoleAsync(tenantId, roleRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result<IReadOnlyCollection<RolePermissionAssignmentResponse>>.Failure(resolved.Error);
        }

        return await GuardAsync("GetRolePermissions", () => _accessControl.GetRolePermissionsAsync(resolved.Value.EntityId, cancellationToken));
    }

    // ----- Shared plumbing ---------------------------------------------------------------------

    // The tenant itself is resolved, and the caller's permission in it checked, by
    // TenantPermissionFilter before the action runs; these find the entity named inside it.

    private async Task<Result<ResolvedEntity>> ResolveRoleAsync(int tenantId, Guid roleRef, CancellationToken cancellationToken)
    {
        var roleId = await _accessControl.FindRoleIdByRefIdAsync(roleRef, tenantId, cancellationToken);
        if (roleId <= 0)
        {
            return Result<ResolvedEntity>.Failure(Error.Forbidden("ROLE_NOT_FOUND", "Role not found in this tenant."));
        }

        return Result<ResolvedEntity>.Success(new ResolvedEntity(tenantId, roleId));
    }

    private async Task<Result<ResolvedEntity>> ResolveGroupAsync(int tenantId, Guid groupRef, CancellationToken cancellationToken)
    {
        var groupId = await _accessControl.FindGroupIdByRefIdAsync(groupRef, tenantId, cancellationToken);
        if (groupId <= 0)
        {
            return Result<ResolvedEntity>.Failure(Error.Forbidden("GROUP_NOT_FOUND", "Group not found in this tenant."));
        }

        return Result<ResolvedEntity>.Success(new ResolvedEntity(tenantId, groupId));
    }

    private Task<Result<ResolvedEntity>> ResolveUserAsync(int tenantId, Guid userRef, CancellationToken cancellationToken) =>
        TenantReferences.ResolveMemberAsync(_tenants, tenantId, userRef, cancellationToken);

    private async Task<Result<(int TenantId, int UserId, int GroupId)>> ResolveUserAndGroupAsync(int tenantId, Guid userRef, Guid groupRef, CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(tenantId, userRef, cancellationToken);
        if (user.IsFailure)
        {
            return Result<(int, int, int)>.Failure(user.Error);
        }

        var groupId = await _accessControl.FindGroupIdByRefIdAsync(groupRef, tenantId, cancellationToken);
        if (groupId <= 0)
        {
            return Result<(int, int, int)>.Failure(Error.Forbidden("GROUP_NOT_FOUND", "Group not found in this tenant."));
        }

        return Result<(int, int, int)>.Success((tenantId, user.Value.EntityId, groupId));
    }

    /// <summary>
    /// Maps an optional workspace reference to an internal ID. A null reference means tenant-wide,
    /// which is a valid scope rather than a missing value.
    /// </summary>
    private async Task<Result<int?>> ResolveWorkspaceScopeAsync(Guid? workspaceRef, CancellationToken cancellationToken)
    {
        if (!workspaceRef.HasValue)
        {
            return Result<int?>.Success(null);
        }

        var workspaceId = await _workspaceProvider.FindIdByRefIdAsync(workspaceRef.Value, cancellationToken);
        if (workspaceId <= 0)
        {
            return Result<int?>.Failure(Error.Forbidden("WORKSPACE_NOT_FOUND", "Workspace not found."));
        }

        // The stored procedures additionally reject a workspace outside the assignment's tenant.
        return Result<int?>.Success(workspaceId);
    }

    /// <summary>
    /// Applies a change to a role's permissions, then announces it twice: to the hosts that cache
    /// effective permissions, and to the audit log with the role's permissions before and after.
    /// </summary>
    private async Task ChangeRolePermissionsAsync(Guid roleRef, ResolvedEntity role, Func<Task> change, CancellationToken cancellationToken)
    {
        var before = await _accessControl.GetRolePermissionsAsync(role.EntityId, cancellationToken);
        await change();
        var after = await _accessControl.GetRolePermissionsAsync(role.EntityId, cancellationToken);

        await _eventBus.PublishAsync(new RolePermissionsChangedEvent(role.EntityId), cancellationToken);

        var (was, now) = (Describe(before), Describe(after));
        if (was != now)
        {
            await _announcer.ToTenantAsync(new RolePermissionsUpdatedEvent(roleRef) { Changes = [new FieldChange("permissions", was, now)] }, role.TenantId, cancellationToken);
        }

        static string Describe(IEnumerable<RolePermissionAssignmentResponse> permissions) =>
            string.Join(",", permissions.Select(p => p.IsDeny ? "!" + p.Slug : p.Slug).Order(StringComparer.Ordinal));
    }

    // Only the database's guards become results (see SqlGuardErrors); a fault propagates to
    // GlobalExceptionMiddleware.
    private Task<Result> GuardAsync(string operation, Func<Task> action, Error? conflictError = null) =>
        SqlGuardErrors.GuardAsync(_logger, operation, action, conflictError);

    private Task<Result<T>> GuardAsync<T>(string operation, Func<Task<T>> action, Error? conflictError = null) =>
        SqlGuardErrors.GuardAsync<T>(_logger, operation, action, conflictError);
}

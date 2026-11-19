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
    private readonly ILogger<AccessControlService> _logger;
    private readonly TenantAnnouncer _announcer;

    public AccessControlService(
        IAccessControlProvider accessControl,
        ITenantProvider tenants,
        IWorkspaceProvider workspaceProvider,
        ILogger<AccessControlService> logger,
        TenantAnnouncer announcer)
    {
        _accessControl = accessControl;
        _tenants = tenants;
        _workspaceProvider = workspaceProvider;
        _logger = logger;
        _announcer = announcer;
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
            _announcer.AccessChanged();
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
            if (await _accessControl.FindGroupIdByRefIdAsync(request.ParentGroupRef.Value, tenantId, cancellationToken) is not { } parentId)
            {
                return Result<GroupResponse>.Failure(TenantReferences.GroupNotFound);
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

    public async Task<Result> DeleteGroupAsync(int callerId, int tenantId, Guid groupRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveGroupAsync(tenantId, groupRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        // Deleting a group that carries roles takes them, denies included, from every member.
        if (await MovesRolesUnallowedAsync(callerId, tenantId, resolved.Value.EntityId, cancellationToken))
        {
            return Result.Failure(RoleGrantGuard.RolesManageRequired);
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

    public async Task<Result> AddUserToGroupAsync(int callerId, int tenantId, Guid userRef, Guid groupRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAndGroupAsync(tenantId, userRef, groupRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        var (_, userId, groupId) = resolved.Value;

        // Joining a group that carries roles (its own or an ancestor's) grants them, so it needs what
        // assigning them needs.
        if (await MovesRolesUnallowedAsync(callerId, tenantId, groupId, cancellationToken))
        {
            return Result.Failure(RoleGrantGuard.RolesManageRequired);
        }

        return await GuardAsync("AddUserToGroup", async () =>
        {
            await _accessControl.InsertUserGroupAsync(userId, groupId, tenantId, cancellationToken);
            _announcer.AccessChanged();
        });
    }

    public async Task<Result> RemoveUserFromGroupAsync(int callerId, int tenantId, Guid userRef, Guid groupRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAndGroupAsync(tenantId, userRef, groupRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        var (_, userId, groupId) = resolved.Value;

        // Leaving a group that carries roles takes them away, a deny the tenant set through it included,
        // as removing the role would.
        if (await MovesRolesUnallowedAsync(callerId, tenantId, groupId, cancellationToken))
        {
            return Result.Failure(RoleGrantGuard.RolesManageRequired);
        }

        return await GuardAsync("RemoveUserFromGroup", async () =>
        {
            await _accessControl.RemoveUserGroupAsync(userId, groupId, tenantId, cancellationToken);
            _announcer.AccessChanged();
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

        var workspace = await ResolveWorkspaceScopeAsync(tenantId, request.WorkspaceRef, cancellationToken);
        if (workspace.IsFailure)
        {
            return Result.Failure(workspace.Error);
        }

        return await GuardAsync("GrantUserPermission", async () =>
        {
            await _accessControl.GrantUserPermissionAsync(resolved.Value.EntityId, request.Slug, request.IsDeny, resolved.Value.TenantId, workspace.Value, cancellationToken);
            _announcer.AccessChanged();
        });
    }

    public async Task<Result> RemoveUserPermissionAsync(int tenantId, Guid userRef, string slug, Guid? workspaceRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAsync(tenantId, userRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        var workspace = await ResolveWorkspaceScopeAsync(tenantId, workspaceRef, cancellationToken);
        if (workspace.IsFailure)
        {
            return Result.Failure(workspace.Error);
        }

        return await GuardAsync("RemoveUserPermission", async () =>
        {
            await _accessControl.RemoveUserPermissionAsync(resolved.Value.EntityId, slug, resolved.Value.TenantId, workspace.Value, cancellationToken);
            _announcer.AccessChanged();
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
        if (await _tenants.FindUserIdByRefIdInTenantAsync(userRef, tenantId, cancellationToken) is not { } userId)
        {
            return Result.Failure(TenantReferences.UserNotFound);
        }

        if (await _accessControl.FindRoleIdByRefIdAsync(roleRef, tenantId, cancellationToken) is not { } roleId)
        {
            return Result.Failure(TenantReferences.RoleNotFound);
        }

        var workspace = await ResolveWorkspaceScopeAsync(tenantId, workspaceRef, cancellationToken);
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

            _announcer.AccessChanged();
            await _announcer.ToTenantAsync(
                assign ? new MemberRoleAssignedEvent(userRef, roleRef, workspaceRef) : new MemberRoleRemovedEvent(userRef, roleRef, workspaceRef),
                tenantId, cancellationToken);
        });
    }

    private async Task<Result> ChangeGroupRoleAsync(int tenantId, Guid groupRef, Guid roleRef, Guid? workspaceRef, bool assign, CancellationToken cancellationToken)
    {
        if (await _accessControl.FindGroupIdByRefIdAsync(groupRef, tenantId, cancellationToken) is not { } groupId)
        {
            return Result.Failure(TenantReferences.GroupNotFound);
        }

        if (await _accessControl.FindRoleIdByRefIdAsync(roleRef, tenantId, cancellationToken) is not { } roleId)
        {
            return Result.Failure(TenantReferences.RoleNotFound);
        }

        var workspace = await ResolveWorkspaceScopeAsync(tenantId, workspaceRef, cancellationToken);
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

            _announcer.AccessChanged();
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

    /// <summary>
    /// True when changing who is in the group would grant or take away roles (it or a group above it
    /// carries some) and the caller may not do that: it needs roles.manage tenant-wide, as assigning or
    /// removing the role directly does.
    /// </summary>
    private async Task<bool> MovesRolesUnallowedAsync(int callerId, int tenantId, int groupId, CancellationToken cancellationToken)
    {
        return await _accessControl.GroupCarriesRolesAsync(groupId, tenantId, cancellationToken)
            && !await RoleGrantGuard.MayGrantRolesAsync(_accessControl, callerId, tenantId, cancellationToken);
    }

    // The tenant itself is resolved, and the caller's permission in it checked, by
    // TenantPermissionFilter before the action runs; these find the entity named inside it.

    private async Task<Result<ResolvedEntity>> ResolveRoleAsync(int tenantId, Guid roleRef, CancellationToken cancellationToken)
    {
        if (await _accessControl.FindRoleIdByRefIdAsync(roleRef, tenantId, cancellationToken) is not { } roleId)
        {
            return Result<ResolvedEntity>.Failure(TenantReferences.RoleNotFound);
        }

        return Result<ResolvedEntity>.Success(new ResolvedEntity(tenantId, roleId));
    }

    private async Task<Result<ResolvedEntity>> ResolveGroupAsync(int tenantId, Guid groupRef, CancellationToken cancellationToken)
    {
        if (await _accessControl.FindGroupIdByRefIdAsync(groupRef, tenantId, cancellationToken) is not { } groupId)
        {
            return Result<ResolvedEntity>.Failure(TenantReferences.GroupNotFound);
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

        if (await _accessControl.FindGroupIdByRefIdAsync(groupRef, tenantId, cancellationToken) is not { } groupId)
        {
            return Result<(int, int, int)>.Failure(TenantReferences.GroupNotFound);
        }

        return Result<(int, int, int)>.Success((tenantId, user.Value.EntityId, groupId));
    }

    /// <summary>The workspace an assignment is scoped to; no reference means tenant-wide, which is a scope rather than a missing value.</summary>
    private async Task<Result<int?>> ResolveWorkspaceScopeAsync(int tenantId, Guid? workspaceRef, CancellationToken cancellationToken)
    {
        if (workspaceRef is not { } reference)
        {
            return Result<int?>.Success(null);
        }

        var workspace = await TenantReferences.ResolveWorkspaceAsync(_workspaceProvider, tenantId, reference, cancellationToken);
        return workspace.IsSuccess ? Result<int?>.Success(workspace.Value) : Result<int?>.Failure(workspace.Error);
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

        _announcer.AccessChanged();

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

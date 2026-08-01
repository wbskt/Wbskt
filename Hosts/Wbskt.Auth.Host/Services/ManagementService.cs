using Microsoft.Data.SqlClient;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Models;
using Wbskt.Primitives.Constants;
using Wbskt.Primitives.Exceptions;
using Wbskt.Primitives.Models;

namespace Wbskt.Auth.Host.Services;

internal sealed class ManagementService : IManagementService
{
    private readonly IAuthProvider _provider;
    private readonly IWorkspaceProvider _workspaceProvider;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ManagementService> _logger;

    public ManagementService(
        IAuthProvider provider,
        IWorkspaceProvider workspaceProvider,
        IEventBus eventBus,
        ILogger<ManagementService> logger)
    {
        _provider = provider;
        _workspaceProvider = workspaceProvider;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyCollection<TenantResponse>>> GetTenantsForUserAsync(int callerId, CancellationToken cancellationToken = default)
    {
        return await GuardAsync("GetTenantsForUser", async () =>
        {
            var tenants = await _provider.GetTenantsForUserAsync(callerId, cancellationToken);
            return (IReadOnlyCollection<TenantResponse>)tenants
                .Select(t => new TenantResponse(t.RefId, t.Name))
                .ToList();
        });
    }

    // ----- Roles -------------------------------------------------------------------------------

    public async Task<Result<IPagedList<RoleResponse>>> GetRolesAsync(int callerId, Guid tenantRef, int skip, int take, CancellationToken cancellationToken = default)
    {
        var scope = await AuthorizeAsync(callerId, tenantRef, Permissions.RolesRead, cancellationToken);
        if (scope.IsFailure)
        {
            return Result<IPagedList<RoleResponse>>.Failure(scope.Error);
        }

        return await GuardAsync("GetRoles", () => _provider.GetRolesAsync(scope.Value, skip, take, cancellationToken));
    }

    public async Task<Result<RoleResponse>> CreateRoleAsync(int callerId, Guid tenantRef, CreateRoleRequest request, CancellationToken cancellationToken = default)
    {
        var scope = await AuthorizeAsync(callerId, tenantRef, Permissions.RolesManage, cancellationToken);
        if (scope.IsFailure)
        {
            return Result<RoleResponse>.Failure(scope.Error);
        }

        return await GuardAsync("CreateRole", async () =>
        {
            var refId = await _provider.InsertRoleAsync(request.Name, request.Description, scope.Value, cancellationToken);
            _logger.LogInformation("Role '{RoleName}' created in tenant {TenantId} with RefId {RefId}", request.Name, scope.Value, refId);
            return new RoleResponse(refId, request.Name, request.Description);
        }, Error.Conflict("AUTH_ROLE_CONFLICT", "A role with that name already exists in this tenant."));
    }

    public async Task<Result> UpdateRoleAsync(int callerId, Guid tenantRef, Guid roleRef, UpdateRoleRequest request, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveRoleAsync(callerId, tenantRef, roleRef, Permissions.RolesManage, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        return await GuardAsync("UpdateRole", () => _provider.UpdateRoleAsync(resolved.Value.EntityId, resolved.Value.TenantId, request.Name, request.Description, cancellationToken),
            Error.Conflict("AUTH_ROLE_CONFLICT", "A role with that name already exists in this tenant."));
    }

    public async Task<Result> DeleteRoleAsync(int callerId, Guid tenantRef, Guid roleRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveRoleAsync(callerId, tenantRef, roleRef, Permissions.RolesManage, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        return await GuardAsync("DeleteRole", async () =>
        {
            await _provider.DeleteRoleAsync(resolved.Value.EntityId, resolved.Value.TenantId, cancellationToken);

            // The role's permissions are gone, so anyone who held it has a different effective set now.
            await _eventBus.PublishAsync(new RolePermissionsChangedEvent(resolved.Value.EntityId), cancellationToken);
        });
    }

    // ----- Groups ------------------------------------------------------------------------------

    public async Task<Result<IPagedList<GroupResponse>>> GetGroupsAsync(int callerId, Guid tenantRef, int skip, int take, CancellationToken cancellationToken = default)
    {
        var scope = await AuthorizeAsync(callerId, tenantRef, Permissions.UsersRead, cancellationToken);
        if (scope.IsFailure)
        {
            return Result<IPagedList<GroupResponse>>.Failure(scope.Error);
        }

        return await GuardAsync("GetGroups", () => _provider.GetGroupsAsync(scope.Value, skip, take, cancellationToken));
    }

    public async Task<Result<GroupResponse>> CreateGroupAsync(int callerId, Guid tenantRef, CreateGroupRequest request, CancellationToken cancellationToken = default)
    {
        var scope = await AuthorizeAsync(callerId, tenantRef, Permissions.UsersManage, cancellationToken);
        if (scope.IsFailure)
        {
            return Result<GroupResponse>.Failure(scope.Error);
        }

        int? parentGroupId = null;
        if (request.ParentGroupRef.HasValue)
        {
            var parentId = await _provider.FindGroupIdByRefIdAsync(request.ParentGroupRef.Value, scope.Value, cancellationToken);
            if (parentId <= 0)
            {
                return Result<GroupResponse>.Failure(Error.Forbidden("GROUP_NOT_FOUND", "Parent group not found in this tenant."));
            }

            parentGroupId = parentId;
        }

        return await GuardAsync("CreateGroup", async () =>
        {
            var refId = await _provider.InsertGroupAsync(request.Name, parentGroupId, scope.Value, cancellationToken);
            _logger.LogInformation("Group '{GroupName}' created in tenant {TenantId} with RefId {RefId}", request.Name, scope.Value, refId);
            return new GroupResponse(refId, request.Name, request.ParentGroupRef);
        }, Error.Conflict("AUTH_GROUP_CONFLICT", "A group with that name already exists in this tenant."));
    }

    public async Task<Result> UpdateGroupAsync(int callerId, Guid tenantRef, Guid groupRef, UpdateGroupRequest request, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveGroupAsync(callerId, tenantRef, groupRef, Permissions.UsersManage, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        return await GuardAsync("UpdateGroup", () => _provider.UpdateGroupAsync(resolved.Value.EntityId, resolved.Value.TenantId, request.Name, cancellationToken),
            Error.Conflict("AUTH_GROUP_CONFLICT", "A group with that name already exists in this tenant."));
    }

    public async Task<Result> DeleteGroupAsync(int callerId, Guid tenantRef, Guid groupRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveGroupAsync(callerId, tenantRef, groupRef, Permissions.UsersManage, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        return await GuardAsync("DeleteGroup", () => _provider.DeleteGroupAsync(resolved.Value.EntityId, resolved.Value.TenantId, cancellationToken));
    }

    // ----- Permission catalogue ----------------------------------------------------------------

    public async Task<Result<IPagedList<PermissionResponse>>> GetPermissionsAsync(int callerId, Guid tenantRef, int skip, int take, CancellationToken cancellationToken = default)
    {
        // The catalogue itself is global; the tenant only scopes the permission check.
        var scope = await AuthorizeAsync(callerId, tenantRef, Permissions.RolesRead, cancellationToken);
        if (scope.IsFailure)
        {
            return Result<IPagedList<PermissionResponse>>.Failure(scope.Error);
        }

        return await GuardAsync("GetPermissions", () => _provider.GetPermissionsAsync(skip, take, cancellationToken));
    }

    // ----- Membership --------------------------------------------------------------------------

    public async Task<Result<IPagedList<TenantMemberResponse>>> GetMembersAsync(int callerId, Guid tenantRef, string? search, int skip, int take, CancellationToken cancellationToken = default)
    {
        var scope = await AuthorizeAsync(callerId, tenantRef, Permissions.UsersRead, cancellationToken);
        if (scope.IsFailure)
        {
            return Result<IPagedList<TenantMemberResponse>>.Failure(scope.Error);
        }

        return await GuardAsync("GetMembers", () => _provider.GetTenantMembersAsync(scope.Value, search, skip, take, cancellationToken));
    }

    public async Task<Result> AddUserToGroupAsync(int callerId, Guid tenantRef, Guid userRef, Guid groupRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAndGroupAsync(callerId, tenantRef, userRef, groupRef, Permissions.UsersManage, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        var (tenantId, userId, groupId) = resolved.Value;
        return await GuardAsync("AddUserToGroup", async () =>
        {
            await _provider.InsertUserGroupAsync(userId, groupId, tenantId, cancellationToken);
            await PublishUserPermissionsChangedAsync(userId, cancellationToken);
        });
    }

    public async Task<Result> RemoveUserFromGroupAsync(int callerId, Guid tenantRef, Guid userRef, Guid groupRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAndGroupAsync(callerId, tenantRef, userRef, groupRef, Permissions.UsersManage, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        var (tenantId, userId, groupId) = resolved.Value;
        return await GuardAsync("RemoveUserFromGroup", async () =>
        {
            await _provider.RemoveUserGroupAsync(userId, groupId, tenantId, cancellationToken);
            await PublishUserPermissionsChangedAsync(userId, cancellationToken);
        });
    }

    public async Task<Result> SetUserActiveAsync(int callerId, Guid tenantRef, Guid userRef, bool isActive, string ipAddress, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAsync(callerId, tenantRef, userRef, Permissions.UsersManage, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        var userId = resolved.Value.EntityId;
        return await GuardAsync("SetUserActive", async () =>
        {
            var user = await _provider.GetByIdAsync(userId, cancellationToken);
            await _provider.SetUserActiveAsync(userId, isActive, cancellationToken);

            // The access token stays valid until it expires, so deactivation only fully takes hold
            // once the refresh tokens are gone and the current access token lapses.
            if (!isActive)
            {
                var revoked = await _provider.RevokeAllRefreshTokensForUserAsync(userId, ipAddress, cancellationToken);
                _logger.LogInformation("Deactivated user ID {UserId} and revoked {RevokedCount} refresh token(s)", userId, revoked);

                await _eventBus.PublishAsync(new SecurityAlertEvent(
                    "UserDeactivated",
                    $"User ID {userId} ({user.Username}) was deactivated and all sessions revoked.",
                    ipAddress,
                    $"UserId: {userId}"), cancellationToken);
            }
        });
    }

    // ----- Permission assignments --------------------------------------------------------------

    public async Task<Result> GrantRolePermissionAsync(int callerId, Guid tenantRef, Guid roleRef, GrantPermissionRequest request, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveRoleAsync(callerId, tenantRef, roleRef, Permissions.RolesManage, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        return await GuardAsync("GrantRolePermission", async () =>
        {
            await _provider.GrantRolePermissionAsync(resolved.Value.EntityId, request.Slug, request.IsDeny, resolved.Value.TenantId, cancellationToken);
            await _eventBus.PublishAsync(new RolePermissionsChangedEvent(resolved.Value.EntityId), cancellationToken);
        });
    }

    public async Task<Result> RemoveRolePermissionAsync(int callerId, Guid tenantRef, Guid roleRef, string slug, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveRoleAsync(callerId, tenantRef, roleRef, Permissions.RolesManage, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        return await GuardAsync("RemoveRolePermission", async () =>
        {
            await _provider.RemoveRolePermissionAsync(resolved.Value.EntityId, slug, resolved.Value.TenantId, cancellationToken);
            await _eventBus.PublishAsync(new RolePermissionsChangedEvent(resolved.Value.EntityId), cancellationToken);
        });
    }

    public async Task<Result> GrantUserPermissionAsync(int callerId, Guid tenantRef, Guid userRef, GrantPermissionRequest request, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAsync(callerId, tenantRef, userRef, Permissions.RolesManage, cancellationToken);
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
            await _provider.GrantUserPermissionAsync(resolved.Value.EntityId, request.Slug, request.IsDeny, resolved.Value.TenantId, workspace.Value, cancellationToken);
            await PublishUserPermissionsChangedAsync(resolved.Value.EntityId, cancellationToken);
        });
    }

    public async Task<Result> RemoveUserPermissionAsync(int callerId, Guid tenantRef, Guid userRef, string slug, Guid? workspaceRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAsync(callerId, tenantRef, userRef, Permissions.RolesManage, cancellationToken);
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
            await _provider.RemoveUserPermissionAsync(resolved.Value.EntityId, slug, resolved.Value.TenantId, workspace.Value, cancellationToken);
            await PublishUserPermissionsChangedAsync(resolved.Value.EntityId, cancellationToken);
        });
    }

    // ----- Role assignments --------------------------------------------------------------------

    public Task<Result> AssignUserRoleAsync(int callerId, Guid tenantRef, Guid userRef, Guid roleRef, Guid? workspaceRef, CancellationToken cancellationToken = default)
        => ChangeUserRoleAsync(callerId, tenantRef, userRef, roleRef, workspaceRef, assign: true, cancellationToken);

    public Task<Result> RemoveUserRoleAsync(int callerId, Guid tenantRef, Guid userRef, Guid roleRef, Guid? workspaceRef, CancellationToken cancellationToken = default)
        => ChangeUserRoleAsync(callerId, tenantRef, userRef, roleRef, workspaceRef, assign: false, cancellationToken);

    public Task<Result> AssignGroupRoleAsync(int callerId, Guid tenantRef, Guid groupRef, Guid roleRef, Guid? workspaceRef, CancellationToken cancellationToken = default)
        => ChangeGroupRoleAsync(callerId, tenantRef, groupRef, roleRef, workspaceRef, assign: true, cancellationToken);

    public Task<Result> RemoveGroupRoleAsync(int callerId, Guid tenantRef, Guid groupRef, Guid roleRef, Guid? workspaceRef, CancellationToken cancellationToken = default)
        => ChangeGroupRoleAsync(callerId, tenantRef, groupRef, roleRef, workspaceRef, assign: false, cancellationToken);

    private async Task<Result> ChangeUserRoleAsync(int callerId, Guid tenantRef, Guid userRef, Guid roleRef, Guid? workspaceRef, bool assign, CancellationToken cancellationToken)
    {
        var scope = await AuthorizeAsync(callerId, tenantRef, Permissions.RolesManage, cancellationToken);
        if (scope.IsFailure)
        {
            return Result.Failure(scope.Error);
        }

        var userId = await _provider.FindUserIdByRefIdInTenantAsync(userRef, scope.Value, cancellationToken);
        if (userId <= 0)
        {
            return Result.Failure(Error.Forbidden("USER_NOT_FOUND", "User not found in this tenant."));
        }

        var roleId = await _provider.FindRoleIdByRefIdAsync(roleRef, scope.Value, cancellationToken);
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
                await _provider.AssignUserRoleAsync(userId, roleId, scope.Value, workspace.Value, cancellationToken);
            }
            else
            {
                await _provider.RemoveUserRoleAsync(userId, roleId, scope.Value, workspace.Value, cancellationToken);
            }

            await PublishUserPermissionsChangedAsync(userId, cancellationToken);
        });
    }

    private async Task<Result> ChangeGroupRoleAsync(int callerId, Guid tenantRef, Guid groupRef, Guid roleRef, Guid? workspaceRef, bool assign, CancellationToken cancellationToken)
    {
        var scope = await AuthorizeAsync(callerId, tenantRef, Permissions.RolesManage, cancellationToken);
        if (scope.IsFailure)
        {
            return Result.Failure(scope.Error);
        }

        var groupId = await _provider.FindGroupIdByRefIdAsync(groupRef, scope.Value, cancellationToken);
        if (groupId <= 0)
        {
            return Result.Failure(Error.Forbidden("GROUP_NOT_FOUND", "Group not found in this tenant."));
        }

        var roleId = await _provider.FindRoleIdByRefIdAsync(roleRef, scope.Value, cancellationToken);
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
                await _provider.AssignGroupRoleAsync(groupId, roleId, scope.Value, workspace.Value, cancellationToken);
            }
            else
            {
                await _provider.RemoveGroupRoleAsync(groupId, roleId, scope.Value, workspace.Value, cancellationToken);
            }

            await _eventBus.PublishAsync(new RolePermissionsChangedEvent(roleId), cancellationToken);
        });
    }

    // ----- Assignment read-back ----------------------------------------------------------------

    public async Task<Result<IReadOnlyCollection<RoleAssignmentResponse>>> GetUserRolesAsync(int callerId, Guid tenantRef, Guid userRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAsync(callerId, tenantRef, userRef, Permissions.RolesRead, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result<IReadOnlyCollection<RoleAssignmentResponse>>.Failure(resolved.Error);
        }

        return await GuardAsync("GetUserRoles", () => _provider.GetUserRolesAsync(resolved.Value.EntityId, resolved.Value.TenantId, cancellationToken));
    }

    public async Task<Result<IReadOnlyCollection<UserPermissionAssignmentResponse>>> GetUserPermissionsAsync(int callerId, Guid tenantRef, Guid userRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAsync(callerId, tenantRef, userRef, Permissions.RolesRead, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result<IReadOnlyCollection<UserPermissionAssignmentResponse>>.Failure(resolved.Error);
        }

        return await GuardAsync("GetUserPermissions", () => _provider.GetUserPermissionsAsync(resolved.Value.EntityId, resolved.Value.TenantId, cancellationToken));
    }

    public async Task<Result<IReadOnlyCollection<GroupMembershipResponse>>> GetUserGroupsAsync(int callerId, Guid tenantRef, Guid userRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAsync(callerId, tenantRef, userRef, Permissions.UsersRead, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result<IReadOnlyCollection<GroupMembershipResponse>>.Failure(resolved.Error);
        }

        return await GuardAsync("GetUserGroups", () => _provider.GetUserGroupsAsync(resolved.Value.EntityId, resolved.Value.TenantId, cancellationToken));
    }

    public async Task<Result<IReadOnlyCollection<RoleAssignmentResponse>>> GetGroupRolesAsync(int callerId, Guid tenantRef, Guid groupRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveGroupAsync(callerId, tenantRef, groupRef, Permissions.RolesRead, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result<IReadOnlyCollection<RoleAssignmentResponse>>.Failure(resolved.Error);
        }

        return await GuardAsync("GetGroupRoles", () => _provider.GetGroupRolesAsync(resolved.Value.EntityId, resolved.Value.TenantId, cancellationToken));
    }

    public async Task<Result<IReadOnlyCollection<RolePermissionAssignmentResponse>>> GetRolePermissionsAsync(int callerId, Guid tenantRef, Guid roleRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveRoleAsync(callerId, tenantRef, roleRef, Permissions.RolesRead, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result<IReadOnlyCollection<RolePermissionAssignmentResponse>>.Failure(resolved.Error);
        }

        return await GuardAsync("GetRolePermissions", () => _provider.GetRolePermissionsAsync(resolved.Value.EntityId, cancellationToken));
    }

    // ----- Shared plumbing ---------------------------------------------------------------------

    private readonly record struct ResolvedEntity(int TenantId, int EntityId);

    /// <summary>
    /// Resolves the tenant reference for the caller and checks they hold <paramref name="permission"/>
    /// tenant-wide within it. A tenant the caller is not a member of does not resolve, so it is
    /// reported identically to one that does not exist.
    /// </summary>
    private async Task<Result<int>> AuthorizeAsync(int callerId, Guid tenantRef, PermissionSlug permission, CancellationToken cancellationToken)
    {
        int tenantId;
        try
        {
            tenantId = await _provider.FindTenantIdByRefIdForUserAsync(tenantRef, callerId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to resolve tenant {TenantRef} for user {UserId}. Error: {Message}", tenantRef, callerId, ex.Message);
            _logger.LogTrace(ex, "Tenant resolution failure for {TenantRef}", tenantRef);
            return Result<int>.Failure(Error.Failure("AUTH_TENANT_ERROR", ex.Message));
        }

        if (tenantId <= 0)
        {
            _logger.LogWarning("Tenant {TenantRef} did not resolve for user ID {UserId}", tenantRef, callerId);
            return Result<int>.Failure(Error.Forbidden("TENANT_NOT_FOUND", "Tenant not found."));
        }

        bool allowed;
        try
        {
            // WorkspaceId null: management operations require the permission tenant-wide, so a
            // workspace-scoped grant deliberately does not satisfy this gate.
            allowed = await _provider.VerifyPermissionAsync(callerId, tenantId, null, permission, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to verify '{Permission}' for user {UserId} in tenant {TenantId}. Error: {Message}", permission, callerId, tenantId, ex.Message);
            _logger.LogTrace(ex, "Permission verification failure for user {UserId}", callerId);
            return Result<int>.Failure(Error.Failure("AUTH_PERMISSION_ERROR", ex.Message));
        }

        if (!allowed)
        {
            _logger.LogWarning("Management operation denied: UserId {UserId} lacks tenant-wide '{Permission}' in TenantId {TenantId}", callerId, permission, tenantId);
            return Result<int>.Failure(Error.Forbidden("PERMISSION_UNAUTHORIZED", $"user does not have permission(s) {permission}"));
        }

        return Result<int>.Success(tenantId);
    }

    private async Task<Result<ResolvedEntity>> ResolveRoleAsync(int callerId, Guid tenantRef, Guid roleRef, PermissionSlug permission, CancellationToken cancellationToken)
    {
        var scope = await AuthorizeAsync(callerId, tenantRef, permission, cancellationToken);
        if (scope.IsFailure)
        {
            return Result<ResolvedEntity>.Failure(scope.Error);
        }

        var roleId = await _provider.FindRoleIdByRefIdAsync(roleRef, scope.Value, cancellationToken);
        if (roleId <= 0)
        {
            return Result<ResolvedEntity>.Failure(Error.Forbidden("ROLE_NOT_FOUND", "Role not found in this tenant."));
        }

        return Result<ResolvedEntity>.Success(new ResolvedEntity(scope.Value, roleId));
    }

    private async Task<Result<ResolvedEntity>> ResolveGroupAsync(int callerId, Guid tenantRef, Guid groupRef, PermissionSlug permission, CancellationToken cancellationToken)
    {
        var scope = await AuthorizeAsync(callerId, tenantRef, permission, cancellationToken);
        if (scope.IsFailure)
        {
            return Result<ResolvedEntity>.Failure(scope.Error);
        }

        var groupId = await _provider.FindGroupIdByRefIdAsync(groupRef, scope.Value, cancellationToken);
        if (groupId <= 0)
        {
            return Result<ResolvedEntity>.Failure(Error.Forbidden("GROUP_NOT_FOUND", "Group not found in this tenant."));
        }

        return Result<ResolvedEntity>.Success(new ResolvedEntity(scope.Value, groupId));
    }

    private async Task<Result<ResolvedEntity>> ResolveUserAsync(int callerId, Guid tenantRef, Guid userRef, PermissionSlug permission, CancellationToken cancellationToken)
    {
        var scope = await AuthorizeAsync(callerId, tenantRef, permission, cancellationToken);
        if (scope.IsFailure)
        {
            return Result<ResolvedEntity>.Failure(scope.Error);
        }

        // Scoped to tenant membership: an administrator cannot reach a user outside their own tenant
        // and pull them into its permission graph.
        var userId = await _provider.FindUserIdByRefIdInTenantAsync(userRef, scope.Value, cancellationToken);
        if (userId <= 0)
        {
            return Result<ResolvedEntity>.Failure(Error.Forbidden("USER_NOT_FOUND", "User not found in this tenant."));
        }

        return Result<ResolvedEntity>.Success(new ResolvedEntity(scope.Value, userId));
    }

    private async Task<Result<(int TenantId, int UserId, int GroupId)>> ResolveUserAndGroupAsync(
        int callerId, Guid tenantRef, Guid userRef, Guid groupRef, PermissionSlug permission, CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(callerId, tenantRef, userRef, permission, cancellationToken);
        if (user.IsFailure)
        {
            return Result<(int, int, int)>.Failure(user.Error);
        }

        var groupId = await _provider.FindGroupIdByRefIdAsync(groupRef, user.Value.TenantId, cancellationToken);
        if (groupId <= 0)
        {
            return Result<(int, int, int)>.Failure(Error.Forbidden("GROUP_NOT_FOUND", "Group not found in this tenant."));
        }

        return Result<(int, int, int)>.Success((user.Value.TenantId, user.Value.EntityId, groupId));
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

    private async Task PublishUserPermissionsChangedAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await _provider.GetByIdAsync(userId, cancellationToken);
        await _eventBus.PublishAsync(new UserPermissionsChangedEvent(userId, user.RefId), cancellationToken);
    }

    // These wrap the provider call so that each operation does not repeat the same try/catch and
    // logging block. Unique-constraint violations are surfaced as a Conflict when the caller passes
    // one, since "that name is taken" is a client error rather than a server fault.
    private async Task<Result> GuardAsync(string operation, Func<Task> action, Error? conflictError = null)
    {
        try
        {
            await action();
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(TranslateException(operation, ex, conflictError));
        }
    }

    private async Task<Result<T>> GuardAsync<T>(string operation, Func<Task<T>> action, Error? conflictError = null)
    {
        try
        {
            return Result<T>.Success(await action());
        }
        catch (Exception ex)
        {
            return Result<T>.Failure(TranslateException(operation, ex, conflictError));
        }
    }

    private Error TranslateException(string operation, Exception ex, Error? conflictError)
    {
        if (conflictError is not null && ex is SqlException { Number: 2601 or 2627 })
        {
            _logger.LogWarning("{Operation} failed: unique constraint violation. Error: {Message}", operation, ex.Message);
            return conflictError;
        }

        if (ex is SecurityException)
        {
            _logger.LogWarning("{Operation} failed: {Message}", operation, ex.Message);
            return Error.NotFound("AUTH_NOT_FOUND", "The requested record was not found.");
        }

        // THROW 50001-50006 from the stored procedures are deliberate guards (wrong tenant, group
        // has children, cannot remove the owner) rather than faults, so they read back as validation.
        if (ex is SqlException { Number: >= 50001 and <= 50006 } guard)
        {
            _logger.LogWarning("{Operation} rejected by database guard: {Message}", operation, guard.Message);
            return Error.Validation("AUTH_OPERATION_REJECTED", guard.Message);
        }

        _logger.LogError("{Operation} failed. Error: {Message}", operation, ex.Message);
        _logger.LogTrace(ex, "{Operation} failure stack trace", operation);
        return Error.Failure("AUTH_MANAGEMENT_ERROR", ex.Message);
    }
}

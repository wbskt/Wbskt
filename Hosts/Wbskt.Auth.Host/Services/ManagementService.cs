using Wbskt.Auth.Host.Services.Email;
using Microsoft.Data.SqlClient;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Security;
using Wbskt.Models;
using Wbskt.Primitives.Constants;
using Wbskt.Primitives.Exceptions;
using Wbskt.Primitives.Models;

namespace Wbskt.Auth.Host.Services;

internal sealed class ManagementService : IManagementService
{
    private const string DefaultWorkspaceName = "Default Workspace";

    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    /// <summary>
    /// One answer for every way an invitation can fail to redeem. An unknown token, an expired one
    /// and one addressed to a different account are indistinguishable, so a token holder cannot use
    /// the endpoint to learn anything about invitations that are not theirs.
    /// </summary>
    private static readonly Error InvalidInvitation =
        Error.Validation("INVITATION_INVALID", "This invitation is not valid. It may have expired, been revoked, already been used, or been sent to a different email address.");

    private readonly IAuthProvider _provider;
    private readonly IWorkspaceProvider _workspaceProvider;
    private readonly IEventBus _eventBus;
    private readonly IAuthMailer _mailer;
    private readonly ILogger<ManagementService> _logger;
    private readonly IAccessTokenRevocation _accessTokens;
    private readonly IAuditWorkspaces _audit;

    public ManagementService(
        IAuthProvider provider,
        IWorkspaceProvider workspaceProvider,
        IEventBus eventBus,
        IAuthMailer mailer,
        ILogger<ManagementService> logger,
        IAccessTokenRevocation accessTokens,
        IAuditWorkspaces? audit = null)
    {
        _audit = audit ?? AuditWorkspaces.None;
        _accessTokens = accessTokens;
        _provider = provider;
        _workspaceProvider = workspaceProvider;
        _eventBus = eventBus;
        _mailer = mailer;
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

    // ----- Tenant lifecycle --------------------------------------------------------------------

    public async Task<Result<TenantResponse>> CreateTenantAsync(int callerId, CreateTenantRequest request, CancellationToken cancellationToken = default)
    {
        // No permission gate, and none is possible: permissions are held within a tenant, so
        // requiring one to create the first tenant would be circular. Authentication is the bar.
        return await GuardAsync("CreateTenant", async () =>
        {
            var refId = await _provider.CreateTenantAsync(request.Name, request.Description, callerId, DefaultWorkspaceName, cancellationToken);
            _logger.LogInformation("Tenant '{TenantName}' created by user ID {CallerId} with RefId {RefId}", request.Name, callerId, refId);
            return new TenantResponse(refId, request.Name);
        });
    }

    public async Task<Result> UpdateTenantAsync(int callerId, Guid tenantRef, UpdateTenantRequest request, CancellationToken cancellationToken = default)
    {
        var scope = await AuthorizeAsync(callerId, tenantRef, Permissions.UsersManage, cancellationToken);
        if (scope.IsFailure)
        {
            return Result.Failure(scope.Error);
        }

        return await GuardAsync("UpdateTenant", () => _provider.UpdateTenantAsync(scope.Value, request.Name, request.Description, cancellationToken));
    }

    public async Task<Result> RemoveTenantMemberAsync(int callerId, Guid tenantRef, Guid userRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAsync(callerId, tenantRef, userRef, Permissions.UsersManage, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        // Workspaces owned by the removed member transfer to the caller, so removing yourself has no
        // defined recipient. Leaving a tenant is a different operation with different semantics and
        // is deliberately not this endpoint.
        if (resolved.Value.EntityId == callerId)
        {
            return Result.Failure(Error.Validation("CANNOT_REMOVE_SELF", "You cannot remove yourself from a tenant."));
        }

        return await GuardAsync("RemoveTenantMember", async () =>
        {
            await _provider.RemoveTenantMemberAsync(resolved.Value.TenantId, resolved.Value.EntityId, callerId, cancellationToken);
            await PublishUserPermissionsChangedAsync(resolved.Value.EntityId, cancellationToken);
            await PublishToTenantAsync(new MemberRemovedEvent(userRef), resolved.Value.TenantId, cancellationToken);
        });
    }

    // ----- Invitations -------------------------------------------------------------------------

    public async Task<Result<CreatedInvitationResponse>> CreateInvitationAsync(int callerId, Guid tenantRef, CreateInvitationRequest request, CancellationToken cancellationToken = default)
    {
        var scope = await AuthorizeAsync(callerId, tenantRef, Permissions.UsersManage, cancellationToken);
        if (scope.IsFailure)
        {
            return Result<CreatedInvitationResponse>.Failure(scope.Error);
        }

        int? roleId = null;
        if (request.RoleRef.HasValue)
        {
            var resolvedRoleId = await _provider.FindRoleIdByRefIdAsync(request.RoleRef.Value, scope.Value, cancellationToken);
            if (resolvedRoleId <= 0)
            {
                return Result<CreatedInvitationResponse>.Failure(Error.Forbidden("ROLE_NOT_FOUND", "Role not found in this tenant."));
            }

            roleId = resolvedRoleId;
        }

        var workspaceIds = new List<int>();
        foreach (var workspaceRef in (request.WorkspaceRefs ?? []).Distinct())
        {
            var workspaceId = await _workspaceProvider.FindIdByRefIdAsync(workspaceRef, cancellationToken);
            if (workspaceId <= 0)
            {
                return Result<CreatedInvitationResponse>.Failure(Error.Forbidden("WORKSPACE_NOT_FOUND", "Workspace not found."));
            }

            // The procedure rejects a workspace outside this tenant (50017).
            workspaceIds.Add(workspaceId);
        }

        var token = SecurityTokens.Generate();
        var expiresAt = DateTime.UtcNow.Add(InvitationLifetime);

        return await GuardAsync("CreateInvitation", async () =>
        {
            var created = await _provider.CreateInvitationAsync(scope.Value, request.Email, roleId, SecurityTokens.Hash(token), expiresAt, callerId, workspaceIds, cancellationToken);
            _logger.LogInformation("Invitation {RefId} issued for tenant {TenantId} by user ID {CallerId}", created.RefId, scope.Value, callerId);

            // Queued, not awaited: an unreachable relay must not fail an invitation that has already
            // been recorded. The administrator still gets the raw token back below and can deliver it
            // by hand, which is how every invitation worked before this host could send mail at all.
            await _mailer.QueueInvitationAsync(request.Email, created.TenantName, token, expiresAt, cancellationToken);
            await PublishToTenantAsync(new InvitationSentEvent(created.RefId, request.Email, request.RoleRef, expiresAt), scope.Value, cancellationToken);

            // The raw token appears here and nowhere else — not in the store, and not in this log line.
            return new CreatedInvitationResponse(created.RefId, request.Email, expiresAt, token);
        });
    }

    public async Task<Result<IPagedList<InvitationResponse>>> GetInvitationsAsync(int callerId, Guid tenantRef, int skip, int take, CancellationToken cancellationToken = default)
    {
        var scope = await AuthorizeAsync(callerId, tenantRef, Permissions.UsersRead, cancellationToken);
        if (scope.IsFailure)
        {
            return Result<IPagedList<InvitationResponse>>.Failure(scope.Error);
        }

        return await GuardAsync("GetInvitations", () => _provider.GetInvitationsAsync(scope.Value, skip, take, cancellationToken));
    }

    public async Task<Result> RevokeInvitationAsync(int callerId, Guid tenantRef, Guid invitationRef, CancellationToken cancellationToken = default)
    {
        var scope = await AuthorizeAsync(callerId, tenantRef, Permissions.UsersManage, cancellationToken);
        if (scope.IsFailure)
        {
            return Result.Failure(scope.Error);
        }

        // Scoped by tenant in the procedure, so an invitation belonging to another tenant simply
        // matches nothing. Revoking an already-spent invitation is a no-op rather than an error:
        // the caller's intent — that this invitation cannot be redeemed — already holds.
        return await GuardAsync("RevokeInvitation", async () =>
        {
            var email = await _provider.RevokeInvitationAsync(invitationRef, scope.Value, cancellationToken);
            if (email is not null)
            {
                await PublishToTenantAsync(new InvitationRevokedEvent(invitationRef, email), scope.Value, cancellationToken);
            }
        });
    }

    public async Task<Result<AcceptInvitationResponse>> AcceptInvitationAsync(int callerId, string token, CancellationToken cancellationToken = default)
    {
        var tokenHash = SecurityTokens.Hash(token);

        InvitationLookup invitation;
        try
        {
            invitation = await _provider.GetInvitationByTokenHashAsync(tokenHash, cancellationToken);
        }
        catch (SecurityException)
        {
            _logger.LogWarning("Invitation acceptance failed for user ID {CallerId}: no invitation matches the presented token", callerId);
            return Result<AcceptInvitationResponse>.Failure(InvalidInvitation);
        }

        // Read back for the tenant name only. The procedure re-checks validity and the address
        // match under a lock, so this is a nicety, never the gate.
        return await GuardAsync("AcceptInvitation", async () =>
        {
            var tenantId = await _provider.AcceptInvitationAsync(tokenHash, callerId, cancellationToken);
            _logger.LogInformation("User ID {CallerId} joined tenant {TenantRef} by invitation", callerId, invitation.TenantRef);

            var joiner = await PublishUserPermissionsChangedAsync(callerId, cancellationToken);
            await PublishToTenantAsync(new InvitationAcceptedEvent(invitation.RefId, invitation.Email, callerId, joiner.RefId), tenantId, cancellationToken);
            return new AcceptInvitationResponse(invitation.TenantRef, invitation.TenantName);
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

    public async Task<Result> SetMemberSuspendedAsync(int callerId, Guid tenantRef, Guid userRef, bool isSuspended, string ipAddress, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAsync(callerId, tenantRef, userRef, Permissions.UsersManage, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        var userId = resolved.Value.EntityId;
        var tenantId = resolved.Value.TenantId;

        // Nobody could lift it: a suspended member holds no users.manage in the tenant.
        if (isSuspended && userId == callerId)
        {
            return Result.Failure(Error.Validation("AUTH_CANNOT_SUSPEND_SELF", "You cannot suspend yourself."));
        }

        return await GuardAsync("SetMemberSuspended", async () =>
        {
            await _provider.SetMemberSuspendedAsync(tenantId, userId, isSuspended, cancellationToken);

            // Access is resolved per request, so the suspension takes effect on the member's next
            // call without touching their tokens - which also still work in their other tenants.
            await PublishUserPermissionsChangedAsync(userId, cancellationToken);
            await PublishToTenantAsync(isSuspended ? new MemberSuspendedEvent(userRef) : new MemberUnsuspendedEvent(userRef), tenantId, cancellationToken);

            if (isSuspended)
            {
                _logger.LogInformation("Suspended user ID {UserId} in tenant ID {TenantId}", userId, tenantId);
                await _eventBus.PublishAsync(new SecurityAlertEvent(
                    "MemberSuspended",
                    $"User ID {userId} was suspended in tenant ID {tenantId} by user ID {callerId}.",
                    ipAddress,
                    $"UserId: {userId}, TenantId: {tenantId}"), cancellationToken);
            }
        });
    }

    // ----- Permission assignments --------------------------------------------------------------

    public async Task<Result> GrantRolePermissionAsync(int callerId, Guid tenantRef, Guid roleRef, GrantRolePermissionRequest request, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveRoleAsync(callerId, tenantRef, roleRef, Permissions.RolesManage, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        return await GuardAsync("GrantRolePermission", () => ChangeRolePermissionsAsync(roleRef, resolved.Value, () =>
            _provider.GrantRolePermissionAsync(resolved.Value.EntityId, request.Slug, request.IsDeny, resolved.Value.TenantId, cancellationToken), cancellationToken));
    }

    public async Task<Result> RemoveRolePermissionAsync(int callerId, Guid tenantRef, Guid roleRef, string slug, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveRoleAsync(callerId, tenantRef, roleRef, Permissions.RolesManage, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        return await GuardAsync("RemoveRolePermission", () => ChangeRolePermissionsAsync(roleRef, resolved.Value, () =>
            _provider.RemoveRolePermissionAsync(resolved.Value.EntityId, slug, resolved.Value.TenantId, cancellationToken), cancellationToken));
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
            await PublishToTenantAsync(
                assign ? new MemberRoleAssignedEvent(userRef, roleRef, workspaceRef) : new MemberRoleRemovedEvent(userRef, roleRef, workspaceRef),
                scope.Value, cancellationToken);
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

    private async Task<User> PublishUserPermissionsChangedAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await _provider.GetByIdAsync(userId, cancellationToken);
        await _eventBus.PublishAsync(new UserPermissionsChangedEvent(userId, user.RefId), cancellationToken);
        return user;
    }

    /// <summary>Publishes an action on the tenant's people, to be logged in each of its workspaces.</summary>
    /// <remarks>
    /// The action has already happened, so a failure to publish is logged rather than returned:
    /// reporting it as failed would only invite a retry of something that succeeded.
    /// </remarks>
    private async Task PublishToTenantAsync(TenantActorEvent @event, int tenantId, CancellationToken cancellationToken)
    {
        try
        {
            await _eventBus.PublishAsync(@event with { WorkspaceIds = await _audit.OfTenantAsync(tenantId, cancellationToken) }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not publish {EventType} for the audit log.", @event.GetType().Name);
        }
    }

    /// <summary>
    /// Applies a change to a role's permissions, then announces it twice: to the hosts that cache
    /// effective permissions, and to the audit log with the role's permissions before and after.
    /// </summary>
    private async Task ChangeRolePermissionsAsync(Guid roleRef, ResolvedEntity role, Func<Task> change, CancellationToken cancellationToken)
    {
        var before = await _provider.GetRolePermissionsAsync(role.EntityId, cancellationToken);
        await change();
        var after = await _provider.GetRolePermissionsAsync(role.EntityId, cancellationToken);

        await _eventBus.PublishAsync(new RolePermissionsChangedEvent(role.EntityId), cancellationToken);

        var (was, now) = (Describe(before), Describe(after));
        if (was != now)
        {
            await PublishToTenantAsync(new RolePermissionsUpdatedEvent(roleRef) { Changes = [new FieldChange("permissions", was, now)] }, role.TenantId, cancellationToken);
        }

        static string Describe(IEnumerable<RolePermissionAssignmentResponse> permissions) =>
            string.Join(",", permissions.Select(p => p.IsDeny ? "!" + p.Slug : p.Slug).Order(StringComparer.Ordinal));
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

        // Redemption lost a race, or the token stopped being valid between the read-back and the
        // procedure. Reported identically to the pre-check path, so which of the two rejected it is
        // not observable from outside.
        if (ex is SqlException { Number: 50011 })
        {
            _logger.LogWarning("{Operation} rejected: invitation no longer valid", operation);
            return InvalidInvitation;
        }

        // A workspace in another tenant reads exactly like one that does not exist, so an invitation
        // cannot be used to learn which workspace references are real elsewhere.
        if (ex is SqlException { Number: 50017 })
        {
            _logger.LogWarning("{Operation} rejected: {Message}", operation, ex.Message);
            return Error.Forbidden("WORKSPACE_NOT_FOUND", "Workspace not found.");
        }

        if (ex is SqlException { Number: 50012 })
        {
            _logger.LogWarning("{Operation} rejected: {Message}", operation, ex.Message);
            return Error.Conflict("AUTH_ALREADY_MEMBER", "That user is already a member of this tenant.");
        }

        // THROW 50001-50019 from the stored procedures are deliberate guards (wrong tenant, group
        // has children, cannot remove the owner or the last administrator, unknown permission slug,
        // expired invitation) rather than faults, so they read back as validation. The upper bound
        // is deliberately ahead of the codes in use: a new guard that lands outside this range
        // reaches the caller as a 500, which is the opposite of what a guard is for.
        if (ex is SqlException { Number: >= 50001 and <= 50019 } guard)
        {
            _logger.LogWarning("{Operation} rejected by database guard: {Message}", operation, guard.Message);
            return Error.Validation("AUTH_OPERATION_REJECTED", guard.Message);
        }

        _logger.LogError("{Operation} failed. Error: {Message}", operation, ex.Message);
        _logger.LogTrace(ex, "{Operation} failure stack trace", operation);
        return Error.Failure("AUTH_MANAGEMENT_ERROR", ex.Message);
    }
}

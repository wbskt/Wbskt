using Wbskt.Auth.Host.Services.Email;
using Microsoft.Data.SqlClient;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Models;
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
    private static readonly Error InvalidInvitation = SqlGuardErrors.InvalidInvitation;

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

    public async Task<Result> UpdateTenantAsync(int tenantId, UpdateTenantRequest request, CancellationToken cancellationToken = default)
    {
        return await GuardAsync("UpdateTenant", () => _provider.UpdateTenantAsync(tenantId, request.Name, request.Description, cancellationToken));
    }

    public async Task<Result> RemoveTenantMemberAsync(int callerId, int tenantId, Guid userRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAsync(tenantId, userRef, cancellationToken);
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

    public async Task<Result<CreatedInvitationResponse>> CreateInvitationAsync(int callerId, int tenantId, CreateInvitationRequest request, CancellationToken cancellationToken = default)
    {
        int? roleId = null;
        if (request.RoleRef.HasValue)
        {
            var resolvedRoleId = await _provider.FindRoleIdByRefIdAsync(request.RoleRef.Value, tenantId, cancellationToken);
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
            var created = await _provider.CreateInvitationAsync(tenantId, request.Email, roleId, SecurityTokens.Hash(token), expiresAt, callerId, workspaceIds, cancellationToken);
            _logger.LogInformation("Invitation {RefId} issued for tenant {TenantId} by user ID {CallerId}", created.RefId, tenantId, callerId);

            // Queued, not awaited: an unreachable relay must not fail an invitation that has already
            // been recorded. The administrator still gets the raw token back below and can deliver it
            // by hand, which is how every invitation worked before this host could send mail at all.
            await _mailer.QueueInvitationAsync(request.Email, created.TenantName, token, expiresAt, cancellationToken);
            await PublishToTenantAsync(new InvitationSentEvent(created.RefId, request.Email, request.RoleRef, expiresAt), tenantId, cancellationToken);

            // The raw token appears here and nowhere else — not in the store, and not in this log line.
            return new CreatedInvitationResponse(created.RefId, request.Email, expiresAt, token);
        });
    }

    public async Task<Result<IPagedList<InvitationResponse>>> GetInvitationsAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await GuardAsync("GetInvitations", () => _provider.GetInvitationsAsync(tenantId, skip, take, cancellationToken));
    }

    public async Task<Result> RevokeInvitationAsync(int tenantId, Guid invitationRef, CancellationToken cancellationToken = default)
    {
        // Scoped by tenant in the procedure, so an invitation belonging to another tenant simply
        // matches nothing. Revoking an already-spent invitation is a no-op rather than an error:
        // the caller's intent — that this invitation cannot be redeemed — already holds.
        return await GuardAsync("RevokeInvitation", async () =>
        {
            var email = await _provider.RevokeInvitationAsync(invitationRef, tenantId, cancellationToken);
            if (email is not null)
            {
                await PublishToTenantAsync(new InvitationRevokedEvent(invitationRef, email), tenantId, cancellationToken);
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

    public async Task<Result<IPagedList<RoleResponse>>> GetRolesAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await GuardAsync("GetRoles", () => _provider.GetRolesAsync(tenantId, skip, take, cancellationToken));
    }

    public async Task<Result<RoleResponse>> CreateRoleAsync(int tenantId, CreateRoleRequest request, CancellationToken cancellationToken = default)
    {
        return await GuardAsync("CreateRole", async () =>
        {
            var refId = await _provider.InsertRoleAsync(request.Name, request.Description, tenantId, cancellationToken);
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

        return await GuardAsync("UpdateRole", () => _provider.UpdateRoleAsync(resolved.Value.EntityId, resolved.Value.TenantId, request.Name, request.Description, cancellationToken),
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
            await _provider.DeleteRoleAsync(resolved.Value.EntityId, resolved.Value.TenantId, cancellationToken);

            // The role's permissions are gone, so anyone who held it has a different effective set now.
            await _eventBus.PublishAsync(new RolePermissionsChangedEvent(resolved.Value.EntityId), cancellationToken);
        });
    }

    // ----- Groups ------------------------------------------------------------------------------

    public async Task<Result<IPagedList<GroupResponse>>> GetGroupsAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await GuardAsync("GetGroups", () => _provider.GetGroupsAsync(tenantId, skip, take, cancellationToken));
    }

    public async Task<Result<GroupResponse>> CreateGroupAsync(int tenantId, CreateGroupRequest request, CancellationToken cancellationToken = default)
    {
        int? parentGroupId = null;
        if (request.ParentGroupRef.HasValue)
        {
            var parentId = await _provider.FindGroupIdByRefIdAsync(request.ParentGroupRef.Value, tenantId, cancellationToken);
            if (parentId <= 0)
            {
                return Result<GroupResponse>.Failure(Error.Forbidden("GROUP_NOT_FOUND", "Parent group not found in this tenant."));
            }

            parentGroupId = parentId;
        }

        return await GuardAsync("CreateGroup", async () =>
        {
            var refId = await _provider.InsertGroupAsync(request.Name, parentGroupId, tenantId, cancellationToken);
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

        return await GuardAsync("UpdateGroup", () => _provider.UpdateGroupAsync(resolved.Value.EntityId, resolved.Value.TenantId, request.Name, cancellationToken),
            Error.Conflict("AUTH_GROUP_CONFLICT", "A group with that name already exists in this tenant."));
    }

    public async Task<Result> DeleteGroupAsync(int tenantId, Guid groupRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveGroupAsync(tenantId, groupRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        return await GuardAsync("DeleteGroup", () => _provider.DeleteGroupAsync(resolved.Value.EntityId, resolved.Value.TenantId, cancellationToken));
    }

    // ----- Permission catalogue ----------------------------------------------------------------

    public async Task<Result<IPagedList<PermissionResponse>>> GetPermissionsAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default)
    {
        // The catalogue itself is global; the tenant only scopes the permission check.
        return await GuardAsync("GetPermissions", () => _provider.GetPermissionsAsync(skip, take, cancellationToken));
    }

    // ----- Membership --------------------------------------------------------------------------

    public async Task<Result<IPagedList<TenantMemberResponse>>> GetMembersAsync(int tenantId, string? search, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await GuardAsync("GetMembers", () => _provider.GetTenantMembersAsync(tenantId, search, skip, take, cancellationToken));
    }

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
            await _provider.InsertUserGroupAsync(userId, groupId, tenantId, cancellationToken);
            await PublishUserPermissionsChangedAsync(userId, cancellationToken);
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
            await _provider.RemoveUserGroupAsync(userId, groupId, tenantId, cancellationToken);
            await PublishUserPermissionsChangedAsync(userId, cancellationToken);
        });
    }

    public async Task<Result> SetMemberSuspendedAsync(int callerId, int tenantId, Guid userRef, bool isSuspended, string ipAddress, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAsync(tenantId, userRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        var userId = resolved.Value.EntityId;

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

    public async Task<Result> GrantRolePermissionAsync(int tenantId, Guid roleRef, GrantRolePermissionRequest request, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveRoleAsync(tenantId, roleRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        return await GuardAsync("GrantRolePermission", () => ChangeRolePermissionsAsync(roleRef, resolved.Value, () =>
            _provider.GrantRolePermissionAsync(resolved.Value.EntityId, request.Slug, request.IsDeny, resolved.Value.TenantId, cancellationToken), cancellationToken));
    }

    public async Task<Result> RemoveRolePermissionAsync(int tenantId, Guid roleRef, string slug, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveRoleAsync(tenantId, roleRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure(resolved.Error);
        }

        return await GuardAsync("RemoveRolePermission", () => ChangeRolePermissionsAsync(roleRef, resolved.Value, () =>
            _provider.RemoveRolePermissionAsync(resolved.Value.EntityId, slug, resolved.Value.TenantId, cancellationToken), cancellationToken));
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
            await _provider.GrantUserPermissionAsync(resolved.Value.EntityId, request.Slug, request.IsDeny, resolved.Value.TenantId, workspace.Value, cancellationToken);
            await PublishUserPermissionsChangedAsync(resolved.Value.EntityId, cancellationToken);
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
            await _provider.RemoveUserPermissionAsync(resolved.Value.EntityId, slug, resolved.Value.TenantId, workspace.Value, cancellationToken);
            await PublishUserPermissionsChangedAsync(resolved.Value.EntityId, cancellationToken);
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
        var userId = await _provider.FindUserIdByRefIdInTenantAsync(userRef, tenantId, cancellationToken);
        if (userId <= 0)
        {
            return Result.Failure(Error.Forbidden("USER_NOT_FOUND", "User not found in this tenant."));
        }

        var roleId = await _provider.FindRoleIdByRefIdAsync(roleRef, tenantId, cancellationToken);
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
                await _provider.AssignUserRoleAsync(userId, roleId, tenantId, workspace.Value, cancellationToken);
            }
            else
            {
                await _provider.RemoveUserRoleAsync(userId, roleId, tenantId, workspace.Value, cancellationToken);
            }

            await PublishUserPermissionsChangedAsync(userId, cancellationToken);
            await PublishToTenantAsync(
                assign ? new MemberRoleAssignedEvent(userRef, roleRef, workspaceRef) : new MemberRoleRemovedEvent(userRef, roleRef, workspaceRef),
                tenantId, cancellationToken);
        });
    }

    private async Task<Result> ChangeGroupRoleAsync(int tenantId, Guid groupRef, Guid roleRef, Guid? workspaceRef, bool assign, CancellationToken cancellationToken)
    {
        var groupId = await _provider.FindGroupIdByRefIdAsync(groupRef, tenantId, cancellationToken);
        if (groupId <= 0)
        {
            return Result.Failure(Error.Forbidden("GROUP_NOT_FOUND", "Group not found in this tenant."));
        }

        var roleId = await _provider.FindRoleIdByRefIdAsync(roleRef, tenantId, cancellationToken);
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
                await _provider.AssignGroupRoleAsync(groupId, roleId, tenantId, workspace.Value, cancellationToken);
            }
            else
            {
                await _provider.RemoveGroupRoleAsync(groupId, roleId, tenantId, workspace.Value, cancellationToken);
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

        return await GuardAsync("GetUserRoles", () => _provider.GetUserRolesAsync(resolved.Value.EntityId, resolved.Value.TenantId, cancellationToken));
    }

    public async Task<Result<IReadOnlyCollection<UserPermissionAssignmentResponse>>> GetUserPermissionsAsync(int tenantId, Guid userRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAsync(tenantId, userRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result<IReadOnlyCollection<UserPermissionAssignmentResponse>>.Failure(resolved.Error);
        }

        return await GuardAsync("GetUserPermissions", () => _provider.GetUserPermissionsAsync(resolved.Value.EntityId, resolved.Value.TenantId, cancellationToken));
    }

    public async Task<Result<IReadOnlyCollection<GroupMembershipResponse>>> GetUserGroupsAsync(int tenantId, Guid userRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveUserAsync(tenantId, userRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result<IReadOnlyCollection<GroupMembershipResponse>>.Failure(resolved.Error);
        }

        return await GuardAsync("GetUserGroups", () => _provider.GetUserGroupsAsync(resolved.Value.EntityId, resolved.Value.TenantId, cancellationToken));
    }

    public async Task<Result<IReadOnlyCollection<RoleAssignmentResponse>>> GetGroupRolesAsync(int tenantId, Guid groupRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveGroupAsync(tenantId, groupRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result<IReadOnlyCollection<RoleAssignmentResponse>>.Failure(resolved.Error);
        }

        return await GuardAsync("GetGroupRoles", () => _provider.GetGroupRolesAsync(resolved.Value.EntityId, resolved.Value.TenantId, cancellationToken));
    }

    public async Task<Result<IReadOnlyCollection<RolePermissionAssignmentResponse>>> GetRolePermissionsAsync(int tenantId, Guid roleRef, CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveRoleAsync(tenantId, roleRef, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result<IReadOnlyCollection<RolePermissionAssignmentResponse>>.Failure(resolved.Error);
        }

        return await GuardAsync("GetRolePermissions", () => _provider.GetRolePermissionsAsync(resolved.Value.EntityId, cancellationToken));
    }

    // ----- Shared plumbing ---------------------------------------------------------------------

    private readonly record struct ResolvedEntity(int TenantId, int EntityId);

    // The tenant itself is resolved, and the caller's permission in it checked, by
    // TenantPermissionFilter before the action runs; these find the entity named inside it.

    private async Task<Result<ResolvedEntity>> ResolveRoleAsync(int tenantId, Guid roleRef, CancellationToken cancellationToken)
    {
        var roleId = await _provider.FindRoleIdByRefIdAsync(roleRef, tenantId, cancellationToken);
        if (roleId <= 0)
        {
            return Result<ResolvedEntity>.Failure(Error.Forbidden("ROLE_NOT_FOUND", "Role not found in this tenant."));
        }

        return Result<ResolvedEntity>.Success(new ResolvedEntity(tenantId, roleId));
    }

    private async Task<Result<ResolvedEntity>> ResolveGroupAsync(int tenantId, Guid groupRef, CancellationToken cancellationToken)
    {
        var groupId = await _provider.FindGroupIdByRefIdAsync(groupRef, tenantId, cancellationToken);
        if (groupId <= 0)
        {
            return Result<ResolvedEntity>.Failure(Error.Forbidden("GROUP_NOT_FOUND", "Group not found in this tenant."));
        }

        return Result<ResolvedEntity>.Success(new ResolvedEntity(tenantId, groupId));
    }

    private async Task<Result<ResolvedEntity>> ResolveUserAsync(int tenantId, Guid userRef, CancellationToken cancellationToken)
    {
        // Scoped to tenant membership: an administrator cannot reach a user outside their own tenant
        // and pull them into its permission graph.
        var userId = await _provider.FindUserIdByRefIdInTenantAsync(userRef, tenantId, cancellationToken);
        if (userId <= 0)
        {
            return Result<ResolvedEntity>.Failure(Error.Forbidden("USER_NOT_FOUND", "User not found in this tenant."));
        }

        return Result<ResolvedEntity>.Success(new ResolvedEntity(tenantId, userId));
    }

    private async Task<Result<(int TenantId, int UserId, int GroupId)>> ResolveUserAndGroupAsync(int tenantId, Guid userRef, Guid groupRef, CancellationToken cancellationToken)
    {
        var user = await ResolveUserAsync(tenantId, userRef, cancellationToken);
        if (user.IsFailure)
        {
            return Result<(int, int, int)>.Failure(user.Error);
        }

        var groupId = await _provider.FindGroupIdByRefIdAsync(groupRef, tenantId, cancellationToken);
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

    // These wrap the provider call so that each operation does not repeat the same guard translation.
    // Only the database's guards become results (see SqlGuardErrors); a fault propagates to
    // GlobalExceptionMiddleware. Unique-constraint violations are a Conflict when the caller passes
    // one, since "that name is taken" is a client error rather than a server fault.
    private async Task<Result> GuardAsync(string operation, Func<Task> action, Error? conflictError = null)
    {
        try
        {
            await action();
            return Result.Success();
        }
        catch (Exception ex) when (SqlGuardErrors.TryTranslate(ex, conflictError, out var error))
        {
            _logger.LogWarning("{Operation} rejected: {Message}", operation, ex.Message);
            return Result.Failure(error);
        }
    }

    private async Task<Result<T>> GuardAsync<T>(string operation, Func<Task<T>> action, Error? conflictError = null)
    {
        try
        {
            return Result<T>.Success(await action());
        }
        catch (Exception ex) when (SqlGuardErrors.TryTranslate(ex, conflictError, out var error))
        {
            _logger.LogWarning("{Operation} rejected: {Message}", operation, ex.Message);
            return Result<T>.Failure(error);
        }
    }
}

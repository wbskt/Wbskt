using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services.Email;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Models;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Auth.Host.Services;

internal sealed class TenantService : ITenantService
{
    private const string DefaultWorkspaceName = "Default Workspace";

    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    /// <summary>
    /// One answer for every way an invitation can fail to redeem. An unknown token, an expired one
    /// and one addressed to a different account are indistinguishable, so a token holder cannot use
    /// the endpoint to learn anything about invitations that are not theirs.
    /// </summary>
    private static readonly Error InvalidInvitation = SqlGuardErrors.InvalidInvitation;

    private readonly ITenantProvider _tenants;
    private readonly IAccessControlProvider _accessControl;
    private readonly IWorkspaceProvider _workspaceProvider;
    private readonly IEventBus _eventBus;
    private readonly IAuthMailer _mailer;
    private readonly ILogger<TenantService> _logger;
    private readonly TenantAnnouncer _announcer;

    public TenantService(
        ITenantProvider tenants,
        IAccessControlProvider accessControl,
        IAccountProvider accounts,
        IWorkspaceProvider workspaceProvider,
        IEventBus eventBus,
        IAuthMailer mailer,
        ILogger<TenantService> logger,
        IAuditWorkspaces? audit = null)
    {
        _tenants = tenants;
        _accessControl = accessControl;
        _workspaceProvider = workspaceProvider;
        _eventBus = eventBus;
        _mailer = mailer;
        _logger = logger;
        _announcer = new TenantAnnouncer(accounts, eventBus, audit ?? AuditWorkspaces.None, logger);
    }

    public async Task<Result<IReadOnlyCollection<TenantResponse>>> GetTenantsForUserAsync(int callerId, CancellationToken cancellationToken = default)
    {
        return await GuardAsync("GetTenantsForUser", async () =>
        {
            var tenants = await _tenants.GetTenantsForUserAsync(callerId, cancellationToken);
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
            var refId = await _tenants.CreateTenantAsync(request.Name, request.Description, callerId, DefaultWorkspaceName, cancellationToken);
            _logger.LogInformation("Tenant '{TenantName}' created by user ID {CallerId} with RefId {RefId}", request.Name, callerId, refId);
            return new TenantResponse(refId, request.Name);
        });
    }

    public async Task<Result> UpdateTenantAsync(int tenantId, UpdateTenantRequest request, CancellationToken cancellationToken = default)
    {
        return await GuardAsync("UpdateTenant", () => _tenants.UpdateTenantAsync(tenantId, request.Name, request.Description, cancellationToken));
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
            await _tenants.RemoveTenantMemberAsync(resolved.Value.TenantId, resolved.Value.EntityId, callerId, cancellationToken);
            await _announcer.UserPermissionsChangedAsync(resolved.Value.EntityId, cancellationToken);
            await _announcer.ToTenantAsync(new MemberRemovedEvent(userRef), resolved.Value.TenantId, cancellationToken);
        });
    }

    // ----- Members -----------------------------------------------------------------------------

    public async Task<Result<IPagedList<TenantMemberResponse>>> GetMembersAsync(int tenantId, string? search, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await GuardAsync("GetMembers", () => _tenants.GetTenantMembersAsync(tenantId, search, skip, take, cancellationToken));
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
            await _tenants.SetMemberSuspendedAsync(tenantId, userId, isSuspended, cancellationToken);

            // Access is resolved per request, so the suspension takes effect on the member's next
            // call without touching their tokens - which also still work in their other tenants.
            await _announcer.UserPermissionsChangedAsync(userId, cancellationToken);
            await _announcer.ToTenantAsync(isSuspended ? new MemberSuspendedEvent(userRef) : new MemberUnsuspendedEvent(userRef), tenantId, cancellationToken);

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

    // ----- Invitations -------------------------------------------------------------------------

    public async Task<Result<CreatedInvitationResponse>> CreateInvitationAsync(int callerId, int tenantId, CreateInvitationRequest request, CancellationToken cancellationToken = default)
    {
        int? roleId = null;
        if (request.RoleRef.HasValue)
        {
            var resolvedRoleId = await _accessControl.FindRoleIdByRefIdAsync(request.RoleRef.Value, tenantId, cancellationToken);
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
            var created = await _tenants.CreateInvitationAsync(tenantId, request.Email, roleId, SecurityTokens.Hash(token), expiresAt, callerId, workspaceIds, cancellationToken);
            _logger.LogInformation("Invitation {RefId} issued for tenant {TenantId} by user ID {CallerId}", created.RefId, tenantId, callerId);

            // Queued, not awaited: an unreachable relay must not fail an invitation that has already
            // been recorded. The administrator still gets the raw token back below and can deliver it
            // by hand, which is how every invitation worked before this host could send mail at all.
            await _mailer.QueueInvitationAsync(request.Email, created.TenantName, token, expiresAt, cancellationToken);
            await _announcer.ToTenantAsync(new InvitationSentEvent(created.RefId, request.Email, request.RoleRef, expiresAt), tenantId, cancellationToken);

            // The raw token appears here and nowhere else — not in the store, and not in this log line.
            return new CreatedInvitationResponse(created.RefId, request.Email, expiresAt, token);
        });
    }

    public async Task<Result<IPagedList<InvitationResponse>>> GetInvitationsAsync(int tenantId, int skip, int take, CancellationToken cancellationToken = default)
    {
        return await GuardAsync("GetInvitations", () => _tenants.GetInvitationsAsync(tenantId, skip, take, cancellationToken));
    }

    public async Task<Result> RevokeInvitationAsync(int tenantId, Guid invitationRef, CancellationToken cancellationToken = default)
    {
        // Scoped by tenant in the procedure, so an invitation belonging to another tenant simply
        // matches nothing. Revoking an already-spent invitation is a no-op rather than an error:
        // the caller's intent — that this invitation cannot be redeemed — already holds.
        return await GuardAsync("RevokeInvitation", async () =>
        {
            var email = await _tenants.RevokeInvitationAsync(invitationRef, tenantId, cancellationToken);
            if (email is not null)
            {
                await _announcer.ToTenantAsync(new InvitationRevokedEvent(invitationRef, email), tenantId, cancellationToken);
            }
        });
    }

    public async Task<Result<AcceptInvitationResponse>> AcceptInvitationAsync(int callerId, string token, CancellationToken cancellationToken = default)
    {
        var tokenHash = SecurityTokens.Hash(token);

        InvitationLookup invitation;
        try
        {
            invitation = await _tenants.GetInvitationByTokenHashAsync(tokenHash, cancellationToken);
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
            var tenantId = await _tenants.AcceptInvitationAsync(tokenHash, callerId, cancellationToken);
            _logger.LogInformation("User ID {CallerId} joined tenant {TenantRef} by invitation", callerId, invitation.TenantRef);

            var joiner = await _announcer.UserPermissionsChangedAsync(callerId, cancellationToken);
            await _announcer.ToTenantAsync(new InvitationAcceptedEvent(invitation.RefId, invitation.Email, callerId, joiner.RefId), tenantId, cancellationToken);
            return new AcceptInvitationResponse(invitation.TenantRef, invitation.TenantName);
        });
    }

    // ----- Shared plumbing ---------------------------------------------------------------------

    private Task<Result<ResolvedEntity>> ResolveUserAsync(int tenantId, Guid userRef, CancellationToken cancellationToken) =>
        TenantReferences.ResolveMemberAsync(_tenants, tenantId, userRef, cancellationToken);

    // Only the database's guards become results (see SqlGuardErrors); a fault propagates to
    // GlobalExceptionMiddleware.
    private Task<Result> GuardAsync(string operation, Func<Task> action, Error? conflictError = null) =>
        SqlGuardErrors.GuardAsync(_logger, operation, action, conflictError);

    private Task<Result<T>> GuardAsync<T>(string operation, Func<Task<T>> action, Error? conflictError = null) =>
        SqlGuardErrors.GuardAsync<T>(_logger, operation, action, conflictError);
}

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Models;
using Wbskt.Primitives.Constants;
using Wbskt.Primitives.Exceptions;
using Wbskt.Primitives.Models;

namespace Wbskt.Auth.Host.Services;

internal sealed class WorkspaceService : IWorkspaceService
{
    private readonly IWorkspaceProvider _workspaceProvider;
    private readonly IAuthProvider _authProvider;
    private readonly IEventBus _eventBus;
    private readonly ILogger<WorkspaceService> _logger;
    private readonly AuthMetrics _metrics;

    public WorkspaceService(
        IWorkspaceProvider workspaceProvider,
        IAuthProvider authProvider,
        IEventBus eventBus,
        ILogger<WorkspaceService> logger,
        AuthMetrics metrics)
    {
        _workspaceProvider = workspaceProvider;
        _authProvider = authProvider;
        _eventBus = eventBus;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<Result<WorkspaceResponse>> CreateWorkspaceAsync(int ownerId, CreateWorkspaceRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting to create workspace '{WorkspaceName}' for owner ID: {OwnerId}", request.Name, ownerId);

        try
        {
            // TODO(arch): multi-tenant users will need an explicit tenant choice; today a user has exactly one tenant.
            var tenants = await _authProvider.GetTenantsForUserAsync(ownerId, cancellationToken);
            var tenant = tenants.FirstOrDefault();
            if (tenant is null)
            {
                _logger.LogWarning("Workspace creation failed: User ID {OwnerId} does not belong to any tenant", ownerId);
                return Result<WorkspaceResponse>.Failure(Error.Validation("TENANT_REQUIRED", "User does not belong to any tenant."));
            }

            var refId = await _workspaceProvider.CreateWorkspaceAsync(request.Name, request.Description ?? string.Empty, ownerId, tenant.Id, cancellationToken);
            _logger.LogInformation("Workspace '{WorkspaceName}' created successfully with RefId: {RefId}", request.Name, refId);
            return Result<WorkspaceResponse>.Success(new WorkspaceResponse(refId, request.Name, request.Description, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to create workspace '{WorkspaceName}' for owner ID: {OwnerId}. Error: {Message}", request.Name, ownerId, ex.Message);
            _logger.LogTrace(ex, "CreateWorkspace failure stack trace for owner ID {OwnerId}", ownerId);
            return Result<WorkspaceResponse>.Failure(Error.Failure("WORKSPACE_CREATE_ERROR", ex.Message));
        }
    }

    public async Task<Result<IReadOnlyCollection<WorkspaceResponse>>> GetWorkspacesForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying workspaces for user ID: {UserId}", userId);

        try
        {
            var workspaces = await _workspaceProvider.GetWorkspacesForUserAsync(userId, cancellationToken);
            _logger.LogTrace("Retrieved {Count} workspaces for user ID: {UserId}", workspaces.Count, userId);
            
            var items = workspaces.Select(w => new WorkspaceResponse(w.RefId, w.Name, w.Description, w.CreatedAt)).ToList() as IReadOnlyCollection<WorkspaceResponse>;
            return Result<IReadOnlyCollection<WorkspaceResponse>>.Success(items);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to query workspaces for user ID: {UserId}. Error: {Message}", userId, ex.Message);
            _logger.LogTrace(ex, "GetWorkspacesForUser failure stack trace for user ID {UserId}", userId);
            return Result<IReadOnlyCollection<WorkspaceResponse>>.Failure(Error.Failure("WORKSPACE_QUERY_ERROR", ex.Message));
        }
    }

    public async Task<Result> AddUserToWorkspaceAsync(int callerId, int workspaceId, AddMemberRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Adding user {UserEmail} to workspace ID: {WorkspaceId}", request.Email, workspaceId);

        // Caller must hold users.manage in this workspace
        var accessResult = await ResolveAccessAsync(callerId, workspaceId, cancellationToken);
        if (accessResult.IsFailure)
        {
            return Result.Failure(accessResult.Error);
        }

        if (!accessResult.Value.Contains(Permissions.UsersManage))
        {
            _logger.LogWarning("Add member denied: caller lacks '{Permission}' in workspace ID: {WorkspaceId}", Permissions.UsersManage, workspaceId);
            return Result.Failure(Error.Forbidden("PERMISSION_UNAUTHORIZED", $"user does not have permission(s) {Permissions.UsersManage}"));
        }

        try
        {
            User user;
            try
            {
                user = await _authProvider.GetByEmailAsync(request.Email, cancellationToken);
            }
            catch (SecurityException ex)
            {
                _logger.LogWarning("Failed to add member to workspace ID {WorkspaceId}: User with email {UserEmail} not found. Error: {Message}", workspaceId, request.Email, ex.Message);
                _logger.LogTrace(ex, "AddUserToWorkspace user not found stack trace for email {UserEmail}", request.Email);
                return Result.Failure(Error.NotFound("USER_NOT_FOUND", $"User with email {request.Email} not found."));
            }

            await _workspaceProvider.AddUserToWorkspaceAsync(workspaceId, user.Id, cancellationToken);
            _logger.LogInformation("Successfully added user ID {UserId} ({UserEmail}) to workspace ID: {WorkspaceId}", user.Id, request.Email, workspaceId);
            return Result.Success();
        }
        catch (SqlException ex) when (ex.Number == 50009)
        {
            // The lookup above is by email across every account in the system, so a caller could
            // otherwise distinguish "no such account" from "account exists in another tenant" and
            // use this endpoint to test whether an address is registered. Both answers are the same
            // NotFound: as far as this tenant is concerned, that user does not exist.
            _logger.LogWarning("Add member denied: user {UserEmail} is not a member of the tenant owning workspace ID {WorkspaceId}", request.Email, workspaceId);
            return Result.Failure(Error.NotFound("USER_NOT_FOUND", $"User with email {request.Email} not found."));
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to add user {UserEmail} to workspace ID: {WorkspaceId}. Error: {Message}", request.Email, workspaceId, ex.Message);
            _logger.LogTrace(ex, "AddUserToWorkspace failure stack trace for email {UserEmail} in workspace ID {WorkspaceId}", request.Email, workspaceId);
            return Result.Failure(Error.Failure("WORKSPACE_MEMBER_ERROR", ex.Message));
        }
    }

    public async Task<Result> RemoveUserFromWorkspaceAsync(int callerId, int workspaceId, Guid userRef, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Removing user {UserRef} from workspace ID: {WorkspaceId}", userRef, workspaceId);

        var gate = await RequirePermissionAsync(callerId, workspaceId, Permissions.UsersManage, cancellationToken);
        if (gate.IsFailure)
        {
            return gate;
        }

        try
        {
            var user = await _authProvider.GetByIdAsync(await ResolveUserIdAsync(userRef, cancellationToken), cancellationToken);
            await _workspaceProvider.RemoveUserFromWorkspaceAsync(workspaceId, user.Id, cancellationToken);
            _logger.LogInformation("Removed user ID {UserId} from workspace ID: {WorkspaceId}", user.Id, workspaceId);
            return Result.Success();
        }
        catch (SecurityException ex)
        {
            _logger.LogWarning("Remove member failed: user {UserRef} not found. Error: {Message}", userRef, ex.Message);
            return Result.Failure(Error.Forbidden("USER_NOT_FOUND", "User not found."));
        }
        catch (SqlException ex) when (ex.Number == 50006)
        {
            // The owner cannot be removed - a workspace with no owner has no route back to being
            // administered.
            _logger.LogWarning("Remove member rejected for workspace ID {WorkspaceId}: {Message}", workspaceId, ex.Message);
            return Result.Failure(Error.Validation("WORKSPACE_OWNER_PROTECTED", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to remove user {UserRef} from workspace ID: {WorkspaceId}. Error: {Message}", userRef, workspaceId, ex.Message);
            _logger.LogTrace(ex, "RemoveUserFromWorkspace failure stack trace for workspace {WorkspaceId}", workspaceId);
            return Result.Failure(Error.Failure("WORKSPACE_MEMBER_ERROR", ex.Message));
        }
    }

    public async Task<Result<IPagedList<TenantMemberResponse>>> GetMembersAsync(int callerId, int workspaceId, int skip, int take, CancellationToken cancellationToken = default)
    {
        var gate = await RequirePermissionAsync(callerId, workspaceId, Permissions.UsersRead, cancellationToken);
        if (gate.IsFailure)
        {
            return Result<IPagedList<TenantMemberResponse>>.Failure(gate.Error);
        }

        try
        {
            var members = await _workspaceProvider.GetWorkspaceMembersAsync(workspaceId, skip, take, cancellationToken);
            return Result<IPagedList<TenantMemberResponse>>.Success(members);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to list members of workspace ID: {WorkspaceId}. Error: {Message}", workspaceId, ex.Message);
            _logger.LogTrace(ex, "GetMembers failure stack trace for workspace {WorkspaceId}", workspaceId);
            return Result<IPagedList<TenantMemberResponse>>.Failure(Error.Failure("WORKSPACE_QUERY_ERROR", ex.Message));
        }
    }

    public async Task<Result> UpdateWorkspaceAsync(int callerId, int workspaceId, CreateWorkspaceRequest request, CancellationToken cancellationToken = default)
    {
        var gate = await RequirePermissionAsync(callerId, workspaceId, Permissions.UsersManage, cancellationToken);
        if (gate.IsFailure)
        {
            return gate;
        }

        try
        {
            await _workspaceProvider.UpdateWorkspaceAsync(workspaceId, request.Name, request.Description, cancellationToken);
            _logger.LogInformation("Updated workspace ID: {WorkspaceId}", workspaceId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to update workspace ID: {WorkspaceId}. Error: {Message}", workspaceId, ex.Message);
            _logger.LogTrace(ex, "UpdateWorkspace failure stack trace for workspace {WorkspaceId}", workspaceId);
            return Result.Failure(Error.Failure("WORKSPACE_UPDATE_ERROR", ex.Message));
        }
    }

    public async Task<Result> TransferOwnershipAsync(int callerId, int workspaceId, Guid newOwnerRef, CancellationToken cancellationToken = default)
    {
        var gate = await RequirePermissionAsync(callerId, workspaceId, Permissions.UsersManage, cancellationToken);
        if (gate.IsFailure)
        {
            return gate;
        }

        try
        {
            var newOwnerId = await ResolveUserIdAsync(newOwnerRef, cancellationToken);
            await _workspaceProvider.SetOwnerAsync(workspaceId, newOwnerId, cancellationToken);
            _logger.LogInformation("Workspace ID {WorkspaceId} transferred to user ID {UserId} by user ID {CallerId}", workspaceId, newOwnerId, callerId);
            return Result.Success();
        }
        catch (Exception ex) when (ex is SecurityException or SqlException { Number: 50009 })
        {
            // Unknown, and known but outside this tenant, answer the same: as far as this tenant is
            // concerned that user does not exist.
            _logger.LogWarning("Ownership transfer of workspace ID {WorkspaceId} refused: user {UserRef} is not in its tenant", workspaceId, newOwnerRef);
            return Result.Failure(Error.NotFound("USER_NOT_FOUND", "User not found."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transfer workspace ID {WorkspaceId} to user {UserRef}", workspaceId, newOwnerRef);
            return Result.Failure(Error.Failure("WORKSPACE_UPDATE_ERROR", ex.Message));
        }
    }

    public async Task<Result> DeleteWorkspaceAsync(int callerId, int workspaceId, CancellationToken cancellationToken = default)
    {
        var gate = await RequirePermissionAsync(callerId, workspaceId, Permissions.UsersManage, cancellationToken);
        if (gate.IsFailure)
        {
            return gate;
        }

        try
        {
            await _workspaceProvider.DeleteWorkspaceAsync(workspaceId, cancellationToken);
            _logger.LogInformation("Deleted workspace ID: {WorkspaceId}", workspaceId);

            // The workspace's policies, devices and workflows live in the main database, which the
            // management host retires on this event. Without it they would keep running for a
            // workspace nobody can see or administer any more.
            //
            // Caught separately: the workspace is already gone, so reporting the request as failed
            // would only invite a retry that 404s. The error log is the signal to retire it by hand
            // (EXEC dbo.Workspace_Retire on the main database).
            try
            {
                await _eventBus.PublishAsync(new WorkspaceDeletedEvent(workspaceId, callerId), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Deleted workspace ID {WorkspaceId} but could not publish WorkspaceDeletedEvent; its devices, policies and workflows are still live until it is retired.", workspaceId);
            }
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to delete workspace ID: {WorkspaceId}. Error: {Message}", workspaceId, ex.Message);
            _logger.LogTrace(ex, "DeleteWorkspace failure stack trace for workspace {WorkspaceId}", workspaceId);
            return Result.Failure(Error.Failure("WORKSPACE_DELETE_ERROR", ex.Message));
        }
    }

    /// <summary>
    /// Membership gate plus a single permission check, for operations on an existing workspace.
    /// </summary>
    private async Task<Result> RequirePermissionAsync(int callerId, int workspaceId, PermissionSlug permission, CancellationToken cancellationToken)
    {
        var accessResult = await ResolveAccessAsync(callerId, workspaceId, cancellationToken);
        if (accessResult.IsFailure)
        {
            return Result.Failure(accessResult.Error);
        }

        if (!accessResult.Value.Contains(permission))
        {
            _logger.LogWarning("Operation denied: caller lacks '{Permission}' in workspace ID: {WorkspaceId}", permission, workspaceId);
            return Result.Failure(Error.Forbidden("PERMISSION_UNAUTHORIZED", $"user does not have permission(s) {permission}"));
        }

        return Result.Success();
    }

    private async Task<int> ResolveUserIdAsync(Guid userRef, CancellationToken cancellationToken)
    {
        var userId = await _authProvider.FindIdByRefIdAsync(userRef, cancellationToken);
        if (userId <= 0)
        {
            throw new SecurityException($"User with reference {userRef} not found.");
        }

        return userId;
    }

    public async Task<Result<IReadOnlyCollection<string>>> ResolveAccessAsync(int userId, int workspaceId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Resolving access for workspace ID: {WorkspaceId}", workspaceId);

        try
        {
            // 1. Verify Membership (hard gate before any permission evaluation)
            var isMember = await _workspaceProvider.VerifyWorkspaceMembershipAsync(userId, workspaceId, cancellationToken);
            if (!isMember)
            {
                _logger.LogWarning("Workspace access resolution failed: User ID {UserId} is not a member of workspace ID: {WorkspaceId}", userId, workspaceId);
                _metrics.RecordPermissionCheck("effective-set", "unauthorized");
                return Result<IReadOnlyCollection<string>>.Failure(Error.Forbidden("WORKSPACE_UNAUTHORIZED", "user does not have permission to this workspace"));
            }

            // 2. Compute the effective permission set (an empty set is still a successful resolution)
            var permissions = await _authProvider.GetEffectivePermissionsAsync(userId, workspaceId, cancellationToken);

            _logger.LogInformation("User ID {UserId} resolved {Count} effective permissions in workspace ID: {WorkspaceId}", userId, permissions.Count, workspaceId);
            _metrics.RecordPermissionCheck("effective-set", "resolved");
            return Result<IReadOnlyCollection<string>>.Success(permissions);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to resolve access for user ID {UserId} in workspace ID: {WorkspaceId}. Error: {Message}", userId, workspaceId, ex.Message);
            _logger.LogTrace(ex, "ResolveAccess failure stack trace for user ID {UserId} in workspace ID {WorkspaceId}", userId, workspaceId);
            _metrics.RecordPermissionCheck("effective-set", "error");
            return Result<IReadOnlyCollection<string>>.Failure(Error.Failure("WORKSPACE_RESOLVE_ERROR", ex.Message));
        }
    }

    public async Task<Result<WorkspaceAccessResolution>> ResolveAccessAsync(int userId, Guid workspaceRef, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Resolving access for workspace reference: {WorkspaceRef}", workspaceRef);

        try
        {
            var resolution = await _workspaceProvider.ResolveAccessAsync(userId, workspaceRef, cancellationToken);
            if (resolution is null)
            {
                return Result<WorkspaceAccessResolution>.Failure(Error.Forbidden("WORKSPACE_NOT_FOUND", "Workspace not found."));
            }

            if (!resolution.IsMember)
            {
                _logger.LogWarning("Workspace access resolution failed: User ID {UserId} is not a member of workspace ID: {WorkspaceId}", userId, resolution.WorkspaceId);
                _metrics.RecordPermissionCheck("effective-set", "unauthorized");
                return Result<WorkspaceAccessResolution>.Failure(Error.Forbidden("WORKSPACE_UNAUTHORIZED", "user does not have permission to this workspace"));
            }

            _logger.LogDebug("User ID {UserId} resolved {Count} effective permissions in workspace ID: {WorkspaceId}", userId, resolution.Permissions.Count, resolution.WorkspaceId);
            _metrics.RecordPermissionCheck("effective-set", "resolved");
            return Result<WorkspaceAccessResolution>.Success(resolution);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to resolve access for user ID {UserId} in workspace {WorkspaceRef}. Error: {Message}", userId, workspaceRef, ex.Message);
            _logger.LogTrace(ex, "ResolveAccess failure stack trace for user ID {UserId} in workspace {WorkspaceRef}", userId, workspaceRef);
            _metrics.RecordPermissionCheck("effective-set", "error");
            return Result<WorkspaceAccessResolution>.Failure(Error.Failure("WORKSPACE_RESOLVE_ERROR", ex.Message));
        }
    }
}

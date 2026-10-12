using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Events.Auth;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Events.Outbox;
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

        var workspaceId = await _workspaceProvider.FindIdByRefIdAsync(refId, cancellationToken);
        await AnnounceAsync(new WorkspaceCreatedEvent(workspaceId, refId, request.Name), cancellationToken);
        return Result<WorkspaceResponse>.Success(new WorkspaceResponse(refId, request.Name, request.Description, DateTime.UtcNow));
    }

    public async Task<Result<IReadOnlyCollection<WorkspaceResponse>>> GetWorkspacesForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Querying workspaces for user ID: {UserId}", userId);

        var workspaces = await _workspaceProvider.GetWorkspacesForUserAsync(userId, cancellationToken);
        _logger.LogTrace("Retrieved {Count} workspaces for user ID: {UserId}", workspaces.Count, userId);
        
        var items = workspaces.Select(w => new WorkspaceResponse(w.RefId, w.Name, w.Description, w.CreatedAt)).ToList() as IReadOnlyCollection<WorkspaceResponse>;
        return Result<IReadOnlyCollection<WorkspaceResponse>>.Success(items);
    }

    public async Task<Result> AddUserToWorkspaceAsync(int workspaceId, AddMemberRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Adding user {UserEmail} to workspace ID: {WorkspaceId}", request.Email, workspaceId);

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
            await AnnounceAsync(new WorkspaceMemberAddedEvent(workspaceId, user.RefId), cancellationToken);
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
    }

    public async Task<Result> RemoveUserFromWorkspaceAsync(int workspaceId, Guid userRef, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Removing user {UserRef} from workspace ID: {WorkspaceId}", userRef, workspaceId);

        try
        {
            var user = await _authProvider.GetByIdAsync(await ResolveUserIdAsync(userRef, cancellationToken), cancellationToken);
            await _workspaceProvider.RemoveUserFromWorkspaceAsync(workspaceId, user.Id, cancellationToken);
            _logger.LogInformation("Removed user ID {UserId} from workspace ID: {WorkspaceId}", user.Id, workspaceId);
            await AnnounceAsync(new WorkspaceMemberRemovedEvent(workspaceId, user.RefId), cancellationToken);
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
    }

    public async Task<Result<IPagedList<TenantMemberResponse>>> GetMembersAsync(int workspaceId, int skip, int take, CancellationToken cancellationToken = default)
    {
        var members = await _workspaceProvider.GetWorkspaceMembersAsync(workspaceId, skip, take, cancellationToken);
        return Result<IPagedList<TenantMemberResponse>>.Success(members);
    }

    public async Task<Result> UpdateWorkspaceAsync(int workspaceId, CreateWorkspaceRequest request, CancellationToken cancellationToken = default)
    {
        var before = await _workspaceProvider.FindByIdAsync(workspaceId, cancellationToken);
        await _workspaceProvider.UpdateWorkspaceAsync(workspaceId, request.Name, request.Description, cancellationToken);
        _logger.LogInformation("Updated workspace ID: {WorkspaceId}", workspaceId);

        var changes = new List<FieldChange>(2);
        if (!string.Equals(before?.Name, request.Name, StringComparison.Ordinal))
        {
            changes.Add(new FieldChange("name", before?.Name, request.Name));
        }

        if (!string.Equals(before?.Description ?? string.Empty, request.Description ?? string.Empty, StringComparison.Ordinal))
        {
            changes.Add(new FieldChange("description", before?.Description, request.Description));
        }

        if (changes.Count > 0)
        {
            await AnnounceAsync(new WorkspaceUpdatedEvent(workspaceId) { Changes = changes }, cancellationToken);
        }
        return Result.Success();
    }

    public async Task<Result> TransferOwnershipAsync(int callerId, int workspaceId, Guid newOwnerRef, CancellationToken cancellationToken = default)
    {
        try
        {
            var newOwnerId = await ResolveUserIdAsync(newOwnerRef, cancellationToken);
            var before = await _workspaceProvider.FindByIdAsync(workspaceId, cancellationToken);
            await _workspaceProvider.SetOwnerAsync(workspaceId, newOwnerId, cancellationToken);
            _logger.LogInformation("Workspace ID {WorkspaceId} transferred to user ID {UserId} by user ID {CallerId}", workspaceId, newOwnerId, callerId);

            var previousOwnerRef = before is null ? (Guid?)null : (await _authProvider.GetByIdAsync(before.OwnerUserId, cancellationToken)).RefId;
            await AnnounceAsync(
                new WorkspaceOwnershipTransferredEvent(workspaceId, newOwnerRef) { Changes = [new FieldChange("owner", previousOwnerRef?.ToString(), newOwnerRef.ToString())] },
                cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is SecurityException or SqlException { Number: 50009 })
        {
            // Unknown, and known but outside this tenant, answer the same: as far as this tenant is
            // concerned that user does not exist.
            _logger.LogWarning("Ownership transfer of workspace ID {WorkspaceId} refused: user {UserRef} is not in its tenant", workspaceId, newOwnerRef);
            return Result.Failure(Error.NotFound("USER_NOT_FOUND", "User not found."));
        }
    }

    public async Task<Result> DeleteWorkspaceAsync(int callerId, int workspaceId, CancellationToken cancellationToken = default)
    {
        // The workspace's policies, devices and workflows live in the main database, which the
        // management host retires on this event. It is written to the outbox in the delete's own
        // transaction (see OutboxDispatcher), so a broker outage or a restart right after the
        // delete delays the retirement instead of leaving them running.
        await _workspaceProvider.DeleteWorkspaceAsync(workspaceId, OutboxMessage.Create(new WorkspaceDeletedEvent(workspaceId, callerId)), cancellationToken);
        _logger.LogInformation("Deleted workspace ID: {WorkspaceId}", workspaceId);
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

            _logger.LogDebug("User ID {UserId} resolved {Count} effective permissions in workspace ID: {WorkspaceId}", userId, permissions.Count, workspaceId);
            _metrics.RecordPermissionCheck("effective-set", "resolved");
            return Result<IReadOnlyCollection<string>>.Success(permissions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _metrics.RecordPermissionCheck("effective-set", "error");
            throw;
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _metrics.RecordPermissionCheck("effective-set", "error");
            throw;
        }
    }

    /// <summary>
    /// Publishes an audit event for a change that has already been made. A failure is logged, not
    /// returned: reporting the change as failed would only invite a retry of something that happened.
    /// </summary>
    private async Task AnnounceAsync<TEvent>(TEvent @event, CancellationToken cancellationToken) where TEvent : IEvent
    {
        try
        {
            await _eventBus.PublishAsync(@event, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not publish {EventType} for the audit log.", typeof(TEvent).Name);
        }
    }
}

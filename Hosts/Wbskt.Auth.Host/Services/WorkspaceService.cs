using Microsoft.Extensions.Logging;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.Infrastructure;
using Wbskt.Primitives.Constants;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Auth.Host.Services;

internal sealed class WorkspaceService : IWorkspaceService
{
    private readonly IWorkspaceProvider _workspaceProvider;
    private readonly IAuthProvider _authProvider;
    private readonly ILogger<WorkspaceService> _logger;
    private readonly AuthMetrics _metrics;

    public WorkspaceService(
        IWorkspaceProvider workspaceProvider,
        IAuthProvider authProvider,
        ILogger<WorkspaceService> logger,
        AuthMetrics metrics)
    {
        _workspaceProvider = workspaceProvider;
        _authProvider = authProvider;
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
        catch (Exception ex)
        {
            _logger.LogError("Failed to add user {UserEmail} to workspace ID: {WorkspaceId}. Error: {Message}", request.Email, workspaceId, ex.Message);
            _logger.LogTrace(ex, "AddUserToWorkspace failure stack trace for email {UserEmail} in workspace ID {WorkspaceId}", request.Email, workspaceId);
            return Result.Failure(Error.Failure("WORKSPACE_MEMBER_ERROR", ex.Message));
        }
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
}

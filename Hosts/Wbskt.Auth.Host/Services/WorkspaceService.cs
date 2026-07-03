using Microsoft.Extensions.Logging;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Telemetry;
using Wbskt.Infrastructure;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Auth.Host.Services;

internal sealed class WorkspaceService : IWorkspaceService
{
    private readonly IWorkspaceProvider _workspaceProvider;
    private readonly IAuthProvider _authProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<WorkspaceService> _logger;
    private readonly AuthMetrics _metrics;

    public WorkspaceService(
        IWorkspaceProvider workspaceProvider, 
        IAuthProvider authProvider,
        IHttpContextAccessor httpContextAccessor,
        ILogger<WorkspaceService> logger,
        AuthMetrics metrics)
    {
        _workspaceProvider = workspaceProvider;
        _authProvider = authProvider;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<Result<WorkspaceResponse>> CreateWorkspaceAsync(int ownerId, CreateWorkspaceRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting to create workspace '{WorkspaceName}' for owner ID: {OwnerId}", request.Name, ownerId);

        try
        {
            var refId = await _workspaceProvider.CreateWorkspaceAsync(request.Name, request.Description ?? string.Empty, ownerId, cancellationToken);
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

    public async Task<Result> AddUserToWorkspaceAsync(int workspaceId, AddMemberRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Adding user {UserEmail} to workspace ID: {WorkspaceId} (Role: {Role})", request.Email, workspaceId, request.Role);

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

            await _workspaceProvider.AddUserToWorkspaceAsync(workspaceId, user.Id, request.Role, cancellationToken);
            _logger.LogInformation("Successfully added user ID {UserId} ({UserEmail}) to workspace ID: {WorkspaceId} (Role: {Role})", user.Id, request.Email, workspaceId, request.Role);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to add user {UserEmail} to workspace ID: {WorkspaceId}. Error: {Message}", request.Email, workspaceId, ex.Message);
            _logger.LogTrace(ex, "AddUserToWorkspace failure stack trace for email {UserEmail} in workspace ID {WorkspaceId}", request.Email, workspaceId);
            return Result.Failure(Error.Failure("WORKSPACE_MEMBER_ERROR", ex.Message));
        }
    }
    
    public async Task<Result> AuthorizeAsync(int workspaceId, string requiredPermission, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Authorizing permission '{RequiredPermission}' for workspace ID: {WorkspaceId}", requiredPermission, workspaceId);

        var currentUserIdResult = GetCurrentUserId();
        if (currentUserIdResult.IsFailure)
        {
            _logger.LogWarning("Workspace authorization failed: User not authenticated. WorkspaceId: {WorkspaceId}", workspaceId);
            _metrics.RecordPermissionCheck(requiredPermission, "unauthorized");
            return Result.Failure(currentUserIdResult.Error);
        }

        var userId = currentUserIdResult.Value;

        // 1. Verify Membership
        var role = await _workspaceProvider.VerifyWorkspaceMembershipAsync(userId, workspaceId, cancellationToken);
        if (!role.HasValue)
        {
            _logger.LogWarning("Workspace authorization failed: User ID {UserId} is not a member of workspace ID: {WorkspaceId}", userId, workspaceId);
            _metrics.RecordPermissionCheck(requiredPermission, "unauthorized");
            return Result.Failure(Error.Unauthorized("WORKSPACE_UNAUTHORIZED", "user does not have permission to this workspace"));
        }

        // 2. Verify Permission
        var hasPermission = await _authProvider.VerifyPermissionAsync(userId, requiredPermission, cancellationToken);
        if (!hasPermission)
        {
            _logger.LogWarning("Workspace authorization failed: User ID {UserId} lacks permission '{RequiredPermission}' in workspace ID: {WorkspaceId}", userId, requiredPermission, workspaceId);
            _metrics.RecordPermissionCheck(requiredPermission, "denied");
            return Result.Failure(Error.Unauthorized("PERMISSION_UNAUTHORIZED", $"user does not have permission(s) {requiredPermission}"));
        }
        
        _logger.LogInformation("User ID {UserId} successfully authorized with permission '{RequiredPermission}' in workspace ID: {WorkspaceId}", userId, requiredPermission, workspaceId);
        _metrics.RecordPermissionCheck(requiredPermission, "allowed");
        return Result.Success();
    }
    
    private Result<int> GetCurrentUserId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null)
        {
            return Result<int>.Failure(Error.Unauthorized("USER_NOT_AUTHENTICATED", "User not found."));
        }

        var userIdString = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out var userId))
        {
            return Result<int>.Failure(Error.Unauthorized("USER_NOT_AUTHENTICATED", "User not found in token."));
        }
        return Result<int>.Success(userId);
    }
}

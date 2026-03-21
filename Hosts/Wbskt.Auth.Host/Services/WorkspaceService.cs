using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Primitives.Exceptions;

namespace Wbskt.Auth.Host.Services;

internal sealed class WorkspaceService : IWorkspaceService
{
    private readonly IWorkspaceProvider _workspaceProvider;
    private readonly IAuthProvider _authProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public WorkspaceService(
        IWorkspaceProvider workspaceProvider, 
        IAuthProvider authProvider,
        IHttpContextAccessor httpContextAccessor)
    {
        _workspaceProvider = workspaceProvider;
        _authProvider = authProvider;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<WorkspaceResponse> CreateWorkspaceAsync(int ownerId, CreateWorkspaceRequest request, CancellationToken cancellationToken = default)
    {
        var refId = await _workspaceProvider.CreateWorkspaceAsync(request.Name, request.Description ?? string.Empty, ownerId, cancellationToken);
        
        // Placeholder, should fetch the created record
        // TODO: propagate created date to the db
        return new WorkspaceResponse(refId, request.Name, request.Description, DateTime.UtcNow);
    }

    public async Task<IReadOnlyCollection<WorkspaceResponse>> GetWorkspacesForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        var workspaces = await _workspaceProvider.GetWorkspacesForUserAsync(userId, cancellationToken);
        return workspaces.Select(w => new WorkspaceResponse(w.RefId, w.Name, w.Description, w.CreatedAt)).ToList();
    }

    public async Task AddUserToWorkspaceAsync(int workspaceId, AddMemberRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _authProvider.GetByEmailAsync(request.Email, cancellationToken);
        await _workspaceProvider.AddUserToWorkspaceAsync(workspaceId, user.Id, request.Role, cancellationToken);
    }
    
    public async Task AuthorizeAsync(int workspaceId, string requiredPermission, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();

        // 1. Verify Membership
        var role = await _workspaceProvider.VerifyWorkspaceMembershipAsync(userId, workspaceId, cancellationToken);
        if (!role.HasValue)
        {
            throw new SecurityException("Access denied to workspace.");
        }

        // 2. Verify Permission
        // This is a simplified check. A real system would map roles to permissions.
        var hasPermission = await _authProvider.VerifyPermissionAsync(userId, requiredPermission, cancellationToken);
        if (!hasPermission)
        {
            throw new SecurityException($"Missing required permission: {requiredPermission}");
        }
    }
    
    private int GetCurrentUserId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null)
        {
            throw new SecurityException("User not found.");
        }

        var userIdString = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out var userId))
        {
            throw new SecurityException("User not found in token.");
        }
        return userId;
    }
}

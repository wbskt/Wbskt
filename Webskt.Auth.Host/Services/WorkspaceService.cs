using Webskt.Auth.Host.Models;
using Webskt.Auth.Host.Providers;

namespace Webskt.Auth.Host.Services;

internal sealed class WorkspaceService : IWorkspaceService
{
    private readonly IWorkspaceProvider _workspaceProvider;
    private readonly IAuthProvider _authProvider;

    public WorkspaceService(IWorkspaceProvider workspaceProvider, IAuthProvider authProvider)
    {
        _workspaceProvider = workspaceProvider;
        _authProvider = authProvider;
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
}

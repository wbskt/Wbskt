using Wbskt.Auth.Host.Models;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Services;

public interface IWorkspaceService
{
    Task<Result<WorkspaceResponse>> CreateWorkspaceAsync(int ownerId, CreateWorkspaceRequest request, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyCollection<WorkspaceResponse>>> GetWorkspacesForUserAsync(int userId, CancellationToken cancellationToken = default);
    Task<Result> AddUserToWorkspaceAsync(int callerId, int workspaceId, AddMemberRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Membership gate plus effective permission set for <paramref name="userId"/> in the workspace.
    /// The user is an explicit argument rather than being read from the ambient HTTP context, so the
    /// method is callable and testable outside a request.
    /// </summary>
    Task<Result<IReadOnlyCollection<string>>> ResolveAccessAsync(int userId, int workspaceId, CancellationToken cancellationToken = default);
}

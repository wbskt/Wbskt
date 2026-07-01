using Wbskt.Auth.Host.Models;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Services;

public interface IWorkspaceService
{
    Task<WorkspaceResponse> CreateWorkspaceAsync(int ownerId, CreateWorkspaceRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<WorkspaceResponse>> GetWorkspacesForUserAsync(int userId, CancellationToken cancellationToken = default);
    Task AddUserToWorkspaceAsync(int workspaceId, AddMemberRequest request, CancellationToken cancellationToken = default);
    Task<Result> AuthorizeAsync(int workspaceId, string requiredPermission, CancellationToken cancellationToken = default);
}

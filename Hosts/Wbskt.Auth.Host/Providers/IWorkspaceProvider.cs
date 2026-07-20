using Wbskt.Auth.Host.Models;
using Wbskt.Primitives;

namespace Wbskt.Auth.Host.Providers;

internal interface IWorkspaceProvider : IReferenceProvider
{
    Task<Guid> CreateWorkspaceAsync(string name, string description, int ownerId, int tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Workspace>> GetWorkspacesForUserAsync(int userId, CancellationToken cancellationToken = default);
    Task AddUserToWorkspaceAsync(int workspaceId, int userId, CancellationToken cancellationToken = default);
    Task<bool> VerifyWorkspaceMembershipAsync(int userId, int workspaceId, CancellationToken cancellationToken = default);
}

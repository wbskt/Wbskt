using Wbskt.Auth.Host.Models;
using Wbskt.Foundation.Abstraction;

namespace Wbskt.Auth.Host.Providers;

internal interface IWorkspaceProvider : IReferenceProvider
{
    Task<Guid> CreateWorkspaceAsync(string name, string description, int ownerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Workspace>> GetWorkspacesForUserAsync(int userId, CancellationToken cancellationToken = default);
    Task AddUserToWorkspaceAsync(int workspaceId, int userId, byte role, CancellationToken cancellationToken = default);
    Task<byte?> VerifyWorkspaceMembershipAsync(int userId, int workspaceId, CancellationToken cancellationToken = default);
}

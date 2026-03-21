using Wbskt.Primitives.Models;

namespace Wbskt.Management.Host.Services.Clients;

public interface IAuthServiceClient
{
    Task<int> ResolveWorkspaceAsync(Guid workspaceRef, PermissionSlug requiredPermission, CancellationToken cancellationToken = default);
}

public record ResolvedWorkspaceResponse(int WorkspaceId);
public record ResolveWorkspaceRequest(Guid WorkspaceRef, string RequiredPermission);

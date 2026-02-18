namespace Webskt.Management.Host.Services.Clients;

public interface IAuthServiceClient
{
    Task<int> ResolveWorkspaceAsync(Guid workspaceRef, string requiredPermission, CancellationToken cancellationToken = default);
}

public record ResolvedWorkspaceResponse(int WorkspaceId);
public record ResolveWorkspaceRequest(Guid WorkspaceRef, string RequiredPermission);

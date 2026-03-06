namespace Wbskt.Auth.Host.Models;

public record ResolveWorkspaceRequest(Guid WorkspaceRef, string RequiredPermission);
public record ResolvedWorkspaceResponse(int WorkspaceId);

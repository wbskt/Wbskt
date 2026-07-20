namespace Wbskt.Auth.Host.Models;

public record ResolveWorkspaceRequest(Guid WorkspaceRef);
public record ResolvedWorkspaceResponse(int WorkspaceId, string[] Permissions);

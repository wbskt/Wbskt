namespace Wbskt.Auth.Host.Models;

public record ResolveWorkspaceRequest(Guid WorkspaceRef);
public record ResolvedWorkspaceResponse(int WorkspaceId, string[] Permissions);

/// <summary>
/// What <c>dbo.Workspace_ResolveAccess</c> found: the workspace's internal id, whether the user may
/// enter it, and their effective permissions there (always empty when they may not).
/// </summary>
public sealed record WorkspaceAccessResolution(int WorkspaceId, bool IsMember, IReadOnlyCollection<string> Permissions);

namespace Wbskt.Auth.Models;

// The auth host's internal API (InternalRoutes), called by the management host over the backend
// network. Internal ids are their point, so nothing public may carry them.

public record ResolveWorkspaceRequest(Guid WorkspaceRef);

public record ResolvedWorkspaceResponse(int WorkspaceId, string[] Permissions);

/// <summary>The caller's internal user id (<c>InternalRoutes.CurrentUser</c>).</summary>
public record CurrentUserResponse(int UserId);

/// <summary>The workspaces deleted in the window asked for (<c>InternalRoutes.DeletedWorkspaces</c>).</summary>
public record DeletedWorkspacesResponse(int[] WorkspaceIds);

namespace Wbskt.Infrastructure.Authorization;

/// <summary>
/// Resolves a workspace reference for the current caller, for <see cref="WorkspacePermissionFilter"/>.
/// Each host registers its own: the auth host asks its workspace service directly, the management
/// host asks the auth host over HTTP. Both answer an unknown or foreign workspace the same way.
/// </summary>
public interface IWorkspaceAccessResolver
{
    /// <summary>
    /// The workspace's internal id and the permissions the caller holds in it; or the error that refuses
    /// the request (403 for a workspace the caller cannot use, 401 for a caller who cannot be identified).
    /// </summary>
    Task<Result<PermissionScope>> ResolveAsync(Guid workspaceRef, CancellationToken cancellationToken);
}

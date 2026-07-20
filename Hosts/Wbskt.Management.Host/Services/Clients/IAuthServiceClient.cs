using Wbskt.Infrastructure;
using Wbskt.Primitives.Models;

namespace Wbskt.Management.Host.Services.Clients;

public interface IAuthServiceClient
{
    /// <summary>
    /// Resolves a workspace reference to its internal ID together with the caller's
    /// effective permission set in that workspace. Fails if the caller is not a member.
    /// </summary>
    Task<Result<WorkspaceAccess>> ResolveWorkspaceAsync(Guid workspaceRef, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a workspace reference and requires a single permission.
    /// Fails with PERMISSION_UNAUTHORIZED when the caller lacks it.
    /// </summary>
    Task<Result<int>> ResolveWorkspaceAsync(Guid workspaceRef, PermissionSlug requiredPermission, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a workspace reference and requires ALL of the given permissions.
    /// Fails with PERMISSION_UNAUTHORIZED when any is missing.
    /// </summary>
    Task<Result<int>> ResolveWorkspaceAsync(Guid workspaceRef, IReadOnlyCollection<PermissionSlug> requiredPermissions, CancellationToken cancellationToken = default);
}

/// <summary>
/// The caller's resolved access to a workspace: its internal ID and the effective permission slugs.
/// </summary>
public sealed record WorkspaceAccess(int WorkspaceId, IReadOnlySet<string> Permissions)
{
    public bool HasPermission(PermissionSlug permission) => Permissions.Contains(permission);
}

public record ResolvedWorkspaceResponse(int WorkspaceId, string[] Permissions);
public record ResolveWorkspaceRequest(Guid WorkspaceRef);

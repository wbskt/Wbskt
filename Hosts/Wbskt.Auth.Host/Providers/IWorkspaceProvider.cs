using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure.Events.Outbox;
using Wbskt.Models;
using Wbskt.Primitives;

namespace Wbskt.Auth.Host.Providers;

internal interface IWorkspaceProvider : IReferenceProvider
{
    /// <summary>The workspace's id when it is in the tenant, null when it is not or does not exist.</summary>
    Task<int?> FindIdByRefIdInTenantAsync(Guid workspaceRef, int tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the workspace and writes <paramref name="created"/> to the outbox in the same transaction,
    /// with the new workspace's id and reference filled in. Throws 50030 when the tenant already has
    /// <paramref name="workspaceLimit"/> workspaces (null is no limit).
    /// </summary>
    Task<Workspace> CreateWorkspaceAsync(string name, string description, int ownerId, int tenantId, int? workspaceLimit, OutboxMessage created, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Workspace>> GetWorkspacesForUserAsync(int userId, CancellationToken cancellationToken = default);
    Task AddUserToWorkspaceAsync(int workspaceId, int userId, CancellationToken cancellationToken = default);
    /// <summary>False when the user was not a member of the workspace.</summary>
    Task<bool> RemoveUserFromWorkspaceAsync(int workspaceId, int userId, CancellationToken cancellationToken = default);
    Task<IPagedList<TenantMemberResponse>> GetWorkspaceMembersAsync(int workspaceId, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up the workspace, checks membership and computes the effective permission set in one
    /// call. Null when no workspace has that reference.
    /// </summary>
    Task<WorkspaceAccessResolution?> ResolveAccessAsync(int userId, Guid workspaceRef, CancellationToken cancellationToken = default);
    Task UpdateWorkspaceAsync(int workspaceId, string name, string? description, CancellationToken cancellationToken = default);
    /// <summary>Deletes the workspace and, in the same transaction, queues <paramref name="deletedEvent"/> in the outbox.</summary>
    Task DeleteWorkspaceAsync(int workspaceId, OutboxMessage deletedEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes <paramref name="userId"/> the owner, adding them as a member if needed. Throws 50009 when
    /// they are not in the workspace's tenant.
    /// </summary>
    /// <summary>
    /// Hands the workspace to <paramref name="userId"/> (<c>dbo.Workspace_SetOwner</c>) and returns the
    /// previous owner's reference. Throws 50009 when the user is not an active member of its tenant.
    /// </summary>
    Task<Guid> SetOwnerAsync(int workspaceId, int userId, CancellationToken cancellationToken = default);

    /// <summary>The workspace, or null when there is none with that id.</summary>
    Task<Workspace?> FindByIdAsync(int workspaceId, CancellationToken cancellationToken = default);

    /// <summary>Every workspace in the tenant, for the audit log.</summary>
    Task<IReadOnlyCollection<int>> GetIdsByTenantAsync(int tenantId, CancellationToken cancellationToken = default);

    /// <summary>Every workspace the user is a member of, suspended or not, for the audit log.</summary>
    Task<IReadOnlyCollection<int>> GetIdsByUserAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The ids of the workspaces deleted at or after <paramref name="sinceUtc"/> and before
    /// <paramref name="untilUtc"/> (<c>dbo.Workspace_GetDeletedBetween</c>).
    /// </summary>
    Task<IReadOnlyCollection<int>> GetDeletedBetweenAsync(DateTime sinceUtc, DateTime untilUtc, CancellationToken cancellationToken = default);
}

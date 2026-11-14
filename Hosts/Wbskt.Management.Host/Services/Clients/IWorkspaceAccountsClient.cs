using Wbskt.Auth.Models;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services.Clients;

/// <summary>The auth host's workspaces with their tenant's plan (<c>InternalRoutes.WorkspaceAccounts</c>).</summary>
public interface IWorkspaceAccountsClient
{
    /// <summary>
    /// One page, after <paramref name="cursor"/>, of every workspace or only those of
    /// <paramref name="tenantId"/> or the one <paramref name="workspaceId"/>. Throws when the auth host
    /// cannot answer.
    /// </summary>
    Task<Page<WorkspaceAccountResponse>> GetPageAsync(string? cursor, int? tenantId, int? workspaceId, CancellationToken cancellationToken);

    /// <summary>The workspace with that RefId, or null when the auth host has none. Throws when it cannot answer.</summary>
    Task<WorkspaceAccountResponse?> FindAsync(Guid workspaceRef, CancellationToken cancellationToken);
}

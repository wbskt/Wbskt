namespace Wbskt.Management.Host.Providers;

public interface ITenantWorkspaceProvider
{
    /// <summary>
    /// The tenant's workspaces still in use, from their account copies (<c>dbo.WorkspaceAccount_GetBy_TenantId</c>).
    /// Empty for a tenant this database has no copy of.
    /// </summary>
    Task<IReadOnlyCollection<int>> GetWorkspaceIdsAsync(int tenantId, CancellationToken cancellationToken = default);
}

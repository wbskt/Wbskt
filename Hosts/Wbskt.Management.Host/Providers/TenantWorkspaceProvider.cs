using Wbskt.Infrastructure;

namespace Wbskt.Management.Host.Providers;

internal sealed class TenantWorkspaceProvider : BaseSqlProvider, ITenantWorkspaceProvider
{
    public TenantWorkspaceProvider(IConfiguration configuration) : base(configuration) { }

    public async Task<IReadOnlyCollection<int>> GetWorkspaceIdsAsync(int tenantId, CancellationToken cancellationToken = default)
    {
        return await ExecuteCollectionAsync(
            "dbo.WorkspaceAccount_GetBy_TenantId",
            p => p.AddWithValue("@TenantId", tenantId),
            reader => reader.GetInt32(reader.GetOrdinal("WorkspaceId")),
            cancellationToken);
    }
}

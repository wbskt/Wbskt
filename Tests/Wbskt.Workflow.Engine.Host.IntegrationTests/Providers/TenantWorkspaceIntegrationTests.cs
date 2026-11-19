using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Management.Host.Providers;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// <c>dbo.WorkspaceAccount_GetBy_TenantId</c>: the workspaces the management host pushes news about the
/// account to (#955). A deleted workspace and another tenant's are left out.
/// </summary>
[Collection("SqlEdge")]
public sealed class TenantWorkspaceIntegrationTests(SqlEdgeFixture fixture)
{
    [SkippableFact]
    public async Task Lists_the_tenants_workspaces_still_in_use()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Server is not reachable.");
        int tenantId = Random.Shared.Next(1_000_000, int.MaxValue);
        int first = Random.Shared.Next(1_000_000, int.MaxValue);
        await InsertAccountAsync(first, tenantId, retired: false);
        await InsertAccountAsync(first - 1, tenantId, retired: false);
        await InsertAccountAsync(first - 2, tenantId, retired: true);
        await InsertAccountAsync(first - 3, tenantId - 1, retired: false);

        var provider = new TenantWorkspaceProvider(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString })
            .Build());

        Assert.Equal([first - 1, first], await provider.GetWorkspaceIdsAsync(tenantId));
        Assert.Empty(await provider.GetWorkspaceIdsAsync(int.MinValue));
    }

    private async Task InsertAccountAsync(int workspaceId, int tenantId, bool retired)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("""
            INSERT INTO dbo.WorkspaceAccounts (WorkspaceId, TenantId, TenantRefId, PlanCode, PlanRevision, RetiredAt)
            VALUES (@WorkspaceId, @TenantId, NEWID(), N'Free', 1, CASE WHEN @Retired = 1 THEN SYSUTCDATETIME() END);
            """, conn);
        cmd.Parameters.AddWithValue("@WorkspaceId", workspaceId);
        cmd.Parameters.AddWithValue("@TenantId", tenantId);
        cmd.Parameters.AddWithValue("@Retired", retired);
        await cmd.ExecuteNonQueryAsync();
    }
}

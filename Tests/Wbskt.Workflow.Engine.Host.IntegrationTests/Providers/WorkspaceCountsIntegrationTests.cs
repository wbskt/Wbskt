using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Models;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>dbo.Workspace_GetCounts: what a workspace holds, for its card (#666).</summary>
[Collection("SqlEdge")]
public sealed class WorkspaceCountsIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";

    [SkippableFact]
    public async Task Clients_are_counted_by_state_and_deleted_workflows_are_left_out()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        int policyId = Convert.ToInt32(await ScalarAsync("""
            INSERT INTO dbo.RegistrationPolicies (WorkspaceId, Pin, Name) OUTPUT INSERTED.Id
            VALUES (@p0, LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), '-', ''), 20), N'Greenhouse');
            """, workspaceId));
        await ExecAsync("""
            INSERT INTO dbo.Clients (WorkspaceId, PolicyId, Name, SecretHash, Status, IsConnected) VALUES
                (@p0, @p1, N'online', 0x00, 1, 1),
                (@p0, @p1, N'offline', 0x00, 1, 0),
                (@p0, @p1, N'offline 2', 0x00, 1, 0),
                (@p0, @p1, N'pending', 0x00, 0, 0),
                (@p0, @p1, N'revoked', 0x00, 2, 0);
            INSERT INTO dbo.Workflows (RefId, WorkspaceId, State, StateChangedBy) VALUES
                (NEWID(), @p0, 2, 1),
                (NEWID(), @p0, 3, 1),
                (NEWID(), @p0, 4, 1);
            """, workspaceId, policyId);

        var counts = await new WorkspaceCountsProvider(Configuration()).GetAsync(workspaceId, CancellationToken.None);

        Assert.Equal(new WorkspaceCountsResponse(new ClientStateCounts(5, 1, 2, 1, 1, 0), 1, new WorkflowStateCounts(2, 0, 1, 1)), counts);
    }

    [SkippableFact]
    public async Task An_empty_workspace_counts_zeros()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);

        var counts = await new WorkspaceCountsProvider(Configuration()).GetAsync(Random.Shared.Next(1_000_000, int.MaxValue), CancellationToken.None);

        Assert.Equal(new WorkspaceCountsResponse(new ClientStateCounts(0, 0, 0, 0, 0, 0), 0, new WorkflowStateCounts(0, 0, 0, 0)), counts);
    }

    private IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString })
            .Build();

    private async Task ExecAsync(string sql, params object[] args)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = Command(sql, conn, args);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(string sql, params object[] args)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = Command(sql, conn, args);
        return await cmd.ExecuteScalarAsync();
    }

    private static SqlCommand Command(string sql, SqlConnection conn, object[] args)
    {
        var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < args.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        }

        return cmd;
    }
}

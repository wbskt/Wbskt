using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.AuthDatabase;

/// <summary>
/// A tenant's plan (dbo.Tenants.PlanCode) and the procedures around it: dbo.TenantPlan_Set, which
/// writes the change and its event together and refuses one made from a stale read, and
/// dbo.WorkspaceAccount_GetPage, which the management host copies plans from.
/// </summary>
[Collection("SqlEdge")]
public sealed class TenantPlanProcedureTests(AuthSqlFixture fixture)
{
    private const string Skipped = "SQL Server not reachable — skipping.";
    private const string MessageType = "Wbskt.Events.Auth.TenantPlanChangedEvent";

    [SkippableFact]
    public async Task A_new_tenant_is_on_free_at_revision_zero()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = await CreateTenantAsync();

        Assert.Equal("Free", await ScalarAsync<string>("SELECT PlanCode FROM dbo.Tenants WHERE Id = @p0", tenantId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT PlanRevision FROM dbo.Tenants WHERE Id = @p0", tenantId));
    }

    [SkippableFact]
    public async Task Setting_a_plan_raises_the_revision_and_queues_its_event()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = await CreateTenantAsync();
        string payload = $"{{\"tenantId\":{tenantId},\"marker\":\"{Guid.NewGuid():N}\"}}";

        var set = await SetAsync(tenantId, "Business", "{\"clients\":4000}", expectedRevision: 0, payload);

        Assert.NotNull(set);
        Assert.Equal(("Business", "{\"clients\":4000}", 1), set.Value);
        Assert.NotNull(await ScalarAsync<DateTime?>("SELECT PlanChangedAt FROM dbo.Tenants WHERE Id = @p0", tenantId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.OutboxMessages WHERE MessageType = @p0 AND Payload = @p1", MessageType, payload));
    }

    [SkippableFact]
    public async Task A_change_from_a_stale_revision_is_refused_and_queues_nothing()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = await CreateTenantAsync();
        await SetAsync(tenantId, "Maker", null, expectedRevision: 0, "{}");
        string payload = $"{{\"marker\":\"{Guid.NewGuid():N}\"}}";

        var stale = await SetAsync(tenantId, "Pro", null, expectedRevision: 0, payload);

        Assert.Null(stale);
        Assert.Equal("Maker", await ScalarAsync<string>("SELECT PlanCode FROM dbo.Tenants WHERE Id = @p0", tenantId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.OutboxMessages WHERE Payload = @p0", payload));
    }

    [SkippableTheory]
    [InlineData("Team")]
    [InlineData("Business")]
    public async Task Every_plan_code_is_accepted_by_the_table(string planCode)
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = await CreateTenantAsync();

        var set = await SetAsync(tenantId, planCode, null, expectedRevision: 0, "{}");

        Assert.Equal(planCode, set?.PlanCode);
    }

    [SkippableFact]
    public async Task An_unknown_plan_code_is_refused_by_the_table()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = await CreateTenantAsync();

        await Assert.ThrowsAsync<SqlException>(() => SetAsync(tenantId, "Platinum", null, expectedRevision: 0, "{}"));
    }

    [SkippableFact]
    public async Task Workspace_accounts_page_by_workspace_id_and_narrow_to_a_tenant_or_a_workspace()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int tenantId = await CreateTenantAsync();
        int first = await CreateWorkspaceAsync(tenantId);
        int second = await CreateWorkspaceAsync(tenantId);
        int third = await CreateWorkspaceAsync(tenantId);
        await SetAsync(tenantId, "Pro", null, expectedRevision: 0, "{}");

        var page1 = await GetPageAsync(after: null, take: 2, tenantId, workspaceId: null);
        var page2 = await GetPageAsync(after: page1[^1].WorkspaceId, take: 2, tenantId, workspaceId: null);
        var one = await GetPageAsync(after: null, take: 10, tenantId: null, workspaceId: second);

        Assert.Equal([first, second], page1.Select(r => r.WorkspaceId));
        Assert.Equal([third], page2.Select(r => r.WorkspaceId));
        Assert.All(page1.Concat(page2), r => Assert.Equal(("Pro", 1, tenantId), (r.PlanCode, r.PlanRevision, r.TenantId)));
        Assert.Equal([second], one.Select(r => r.WorkspaceId));
    }

    private async Task<(string PlanCode, string? Overrides, int Revision)?> SetAsync(int tenantId, string planCode, string? overrides, int expectedRevision, string payload)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.TenantPlan_Set", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@TenantId", tenantId);
        cmd.Parameters.AddWithValue("@PlanCode", planCode);
        cmd.Parameters.AddWithValue("@PlanLimitOverrides", (object?)overrides ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ExpectedRevision", expectedRevision);
        cmd.Parameters.AddWithValue("@OutboxMessageType", MessageType);
        cmd.Parameters.AddWithValue("@OutboxPayload", payload);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        int ordinal = reader.GetOrdinal("PlanLimitOverrides");
        return (reader.GetString(reader.GetOrdinal("PlanCode")), reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal), reader.GetInt32(reader.GetOrdinal("PlanRevision")));
    }

    private async Task<List<(int WorkspaceId, int TenantId, string PlanCode, int PlanRevision)>> GetPageAsync(int? after, int take, int? tenantId, int? workspaceId)
    {
        var rows = new List<(int, int, string, int)>();
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.WorkspaceAccount_GetPage", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@AfterWorkspaceId", (object?)after ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Take", take);
        cmd.Parameters.AddWithValue("@TenantId", (object?)tenantId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@WorkspaceId", (object?)workspaceId ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetInt32(reader.GetOrdinal("WorkspaceId")), reader.GetInt32(reader.GetOrdinal("TenantId")),
                reader.GetString(reader.GetOrdinal("PlanCode")), reader.GetInt32(reader.GetOrdinal("PlanRevision"))));
        }

        return rows;
    }

    private async Task<int> CreateTenantAsync()
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("INSERT INTO dbo.Tenants (RefId, Name) OUTPUT INSERTED.Id VALUES (NEWID(), @Name);", conn);
        cmd.Parameters.AddWithValue("@Name", $"plan-{Guid.NewGuid():N}"[..30]);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<int> CreateWorkspaceAsync(int tenantId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.Workspace_Create", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Name", $"plan-{Guid.NewGuid():N}"[..30]);
        cmd.Parameters.AddWithValue("@Description", "plan-test");
        cmd.Parameters.AddWithValue("@OwnerUserId", await CreateUserAsync());
        cmd.Parameters.AddWithValue("@TenantId", tenantId);
        var refId = cmd.Parameters.Add("@RefId", SqlDbType.UniqueIdentifier);
        refId.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        return await ScalarAsync<int>("SELECT Id FROM dbo.Workspaces WHERE RefId = @p0", (Guid)refId.Value);
    }

    private async Task<int> CreateUserAsync()
    {
        string unique = $"plan-{Guid.NewGuid():N}";
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(
            """
            INSERT INTO dbo.Users (Username, Email, PasswordHash, IsActive)
            OUTPUT INSERTED.Id
            VALUES (@Username, @Email, 'hash', 1);
            """, conn);
        cmd.Parameters.AddWithValue("@Username", unique[..30]);
        cmd.Parameters.AddWithValue("@Email", $"{unique}@example.test");
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<T> ScalarAsync<T>(string sql, params object[] args)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < args.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        }

        object? value = await cmd.ExecuteScalarAsync();
        return value is DBNull ? default! : (T)value!;
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }
}

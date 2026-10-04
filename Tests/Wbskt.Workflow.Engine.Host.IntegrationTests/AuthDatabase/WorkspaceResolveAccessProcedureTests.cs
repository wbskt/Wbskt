using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.AuthDatabase;

/// <summary>
/// <c>dbo.Workspace_ResolveAccess</c>, the single call behind <c>POST /api/workspaces/resolve</c>. It
/// must give exactly the answers of the three procedures it replaced. Every test builds its own tenant.
/// </summary>
[Collection("SqlEdge")]
public sealed class WorkspaceResolveAccessProcedureTests(AuthSqlFixture fixture)
{
    private const string Skipped = "SQL Server not reachable — skipping.";

    [SkippableFact]
    public async Task A_member_gets_the_same_permissions_as_the_effective_set()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, ownerId) = await CreateTenantAsync();
        var (workspaceId, workspaceRef) = await CreateWorkspaceThroughProcedureAsync(tenantId, ownerId);

        var rows = await ResolveAsync(ownerId, workspaceRef);
        var expected = await EffectiveSetAsync(ownerId, workspaceId);

        Assert.NotEmpty(expected);
        Assert.All(rows, r => Assert.Equal((workspaceId, true), (r.WorkspaceId, r.IsMember)));
        Assert.Equal(expected.Order(), rows.Select(r => r.Slug!).Order());
    }

    [SkippableFact]
    public async Task A_member_without_permissions_gets_one_row_with_no_slug()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, ownerId) = await CreateTenantAsync();
        var (workspaceId, workspaceRef) = await CreateWorkspaceAsync(tenantId, ownerId);
        int memberId = await CreateUserAsync();
        await ExecAsync("INSERT INTO dbo.TenantMembers (TenantId, UserId) VALUES (@p0, @p1)", tenantId, memberId);
        await ExecAsync("INSERT INTO dbo.WorkspaceMembers (WorkspaceId, UserId) VALUES (@p0, @p1)", workspaceId, memberId);

        var row = Assert.Single(await ResolveAsync(memberId, workspaceRef));

        Assert.Equal((workspaceId, true, (string?)null), (row.WorkspaceId, row.IsMember, row.Slug));
    }

    [SkippableFact]
    public async Task A_non_member_gets_one_row_marked_not_a_member()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, ownerId) = await CreateTenantAsync();
        var (workspaceId, workspaceRef) = await CreateWorkspaceThroughProcedureAsync(tenantId, ownerId);
        int outsiderId = await CreateUserAsync();

        var row = Assert.Single(await ResolveAsync(outsiderId, workspaceRef));

        Assert.Equal((workspaceId, false, (string?)null), (row.WorkspaceId, row.IsMember, row.Slug));
    }

    [SkippableFact]
    public async Task A_suspended_member_is_not_a_member()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (tenantId, ownerId) = await CreateTenantAsync();
        var (_, workspaceRef) = await CreateWorkspaceThroughProcedureAsync(tenantId, ownerId);
        await ExecAsync("UPDATE dbo.TenantMembers SET IsSuspended = 1 WHERE TenantId = @p0 AND UserId = @p1", tenantId, ownerId);

        var row = Assert.Single(await ResolveAsync(ownerId, workspaceRef));

        Assert.False(row.IsMember);
        Assert.Null(row.Slug);
    }

    [SkippableFact]
    public async Task An_unknown_workspace_returns_no_rows()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int userId = await CreateUserAsync();

        Assert.Empty(await ResolveAsync(userId, Guid.NewGuid()));
    }

    // ---------------------------------------------------------------- helpers

    private async Task<List<(int WorkspaceId, bool IsMember, string? Slug)>> ResolveAsync(int userId, Guid workspaceRef)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.Workspace_ResolveAccess", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@UserId", userId);
        cmd.Parameters.AddWithValue("@WorkspaceRef", workspaceRef);
        await using var reader = await cmd.ExecuteReaderAsync();

        var rows = new List<(int, bool, string?)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetInt32(0), reader.GetBoolean(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return rows;
    }

    private async Task<List<string>> EffectiveSetAsync(int userId, int workspaceId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.Permission_EffectiveSet", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@UserId", userId);
        cmd.Parameters.AddWithValue("@WorkspaceId", workspaceId);
        await using var reader = await cmd.ExecuteReaderAsync();

        var slugs = new List<string>();
        while (await reader.ReadAsync())
        {
            slugs.Add(reader.GetString(0));
        }

        return slugs;
    }

    private async Task<(int TenantId, int OwnerId)> CreateTenantAsync()
    {
        int ownerId = await CreateUserAsync();
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.Tenant_Create", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Name", "resolve-test");
        cmd.Parameters.AddWithValue("@Description", "resolve-test");
        cmd.Parameters.AddWithValue("@OwnerUserId", ownerId);
        cmd.Parameters.AddWithValue("@WorkspaceName", "Default Workspace");
        cmd.Parameters.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        cmd.Parameters.Add("@TenantId", SqlDbType.Int).Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        return ((int)cmd.Parameters["@TenantId"].Value, ownerId);
    }

    private async Task<(int Id, Guid RefId)> CreateWorkspaceThroughProcedureAsync(int tenantId, int ownerId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.Workspace_Create", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Name", "ws");
        cmd.Parameters.AddWithValue("@Description", "ws");
        cmd.Parameters.AddWithValue("@OwnerUserId", ownerId);
        cmd.Parameters.AddWithValue("@TenantId", tenantId);
        var refId = cmd.Parameters.Add("@RefId", SqlDbType.UniqueIdentifier);
        refId.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        var workspaceRef = (Guid)refId.Value;
        return (await ScalarAsync<int>("SELECT Id FROM dbo.Workspaces WHERE RefId = @p0", workspaceRef), workspaceRef);
    }

    private async Task<(int Id, Guid RefId)> CreateWorkspaceAsync(int tenantId, int ownerId)
    {
        var workspaceRef = Guid.NewGuid();
        int id = await ScalarAsync<int>("""
            INSERT INTO dbo.Workspaces (RefId, TenantId, Name, OwnerUserId) OUTPUT INSERTED.Id VALUES (@p0, @p1, N'ws', @p2);
            """, workspaceRef, tenantId, ownerId);
        return (id, workspaceRef);
    }

    private Task<int> CreateUserAsync()
    {
        string name = $"resolve-{Guid.NewGuid():N}"[..27];
        return ScalarAsync<int>("""
            INSERT INTO dbo.Users (Username, Email, PasswordHash, IsActive) OUTPUT INSERTED.Id VALUES (@p0, @p1, 'hash', 1);
            """, name, $"{name}@example.test");
    }

    private async Task ExecAsync(string sql, params object[] args)
    {
        await using var conn = await OpenAsync();
        await using var cmd = Command(sql, conn, args);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, params object[] args)
    {
        await using var conn = await OpenAsync();
        await using var cmd = Command(sql, conn, args);
        return (T)(await cmd.ExecuteScalarAsync())!;
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

    private async Task<SqlConnection> OpenAsync()
    {
        var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }
}

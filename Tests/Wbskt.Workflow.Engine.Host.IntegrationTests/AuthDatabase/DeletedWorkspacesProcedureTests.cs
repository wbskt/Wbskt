using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.AuthDatabase;

/// <summary>
/// dbo.Workspace_Delete records every delete in dbo.DeletedWorkspaces, and
/// dbo.Workspace_GetDeletedBetween reads them back for the management host's deleted workspace sweep,
/// the backstop for a WorkspaceDeletedEvent that never got its workspace retired.
/// </summary>
[Collection("SqlEdge")]
public sealed class DeletedWorkspacesProcedureTests(AuthSqlFixture fixture)
{
    private const string Skipped = "SQL Server not reachable — skipping.";
    private const string MessageType = "Wbskt.Events.Auth.WorkspaceDeletedEvent";

    [SkippableFact]
    public async Task A_deleted_workspace_is_listed_for_a_window_that_holds_its_delete()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = await CreateWorkspaceAsync();
        DateTime before = await UtcNowAsync();

        await DeleteAsync(workspaceId, withEvent: true);
        DateTime after = (await UtcNowAsync()).AddSeconds(1);

        Assert.Contains(workspaceId, await GetDeletedBetweenAsync(before.AddSeconds(-1), after));
        Assert.DoesNotContain(workspaceId, await GetDeletedBetweenAsync(before.AddDays(-2), before.AddDays(-1)));
        Assert.DoesNotContain(workspaceId, await GetDeletedBetweenAsync(after, after.AddDays(1)));
    }

    [SkippableFact]
    public async Task A_workspace_that_still_exists_is_not_listed()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = await CreateWorkspaceAsync();
        DateTime now = await UtcNowAsync();

        Assert.DoesNotContain(workspaceId, await GetDeletedBetweenAsync(now.AddDays(-1), now.AddDays(1)));
    }

    [SkippableFact]
    public async Task A_delete_without_an_event_is_still_recorded()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = await CreateWorkspaceAsync();

        await DeleteAsync(workspaceId, withEvent: false);

        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.DeletedWorkspaces WHERE WorkspaceId = @p0", workspaceId));
    }

    [SkippableFact]
    public async Task Deleting_twice_records_once_and_does_not_fail()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = await CreateWorkspaceAsync();

        await DeleteAsync(workspaceId, withEvent: true);
        await DeleteAsync(workspaceId, withEvent: true);

        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.DeletedWorkspaces WHERE WorkspaceId = @p0", workspaceId));
    }

    private async Task<List<int>> GetDeletedBetweenAsync(DateTime since, DateTime until)
    {
        var ids = new List<int>();
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.Workspace_GetDeletedBetween", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.Add("@Since", SqlDbType.DateTime2).Value = since;
        cmd.Parameters.Add("@Until", SqlDbType.DateTime2).Value = until;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetInt32(reader.GetOrdinal("WorkspaceId")));
        }

        return ids;
    }

    private async Task<int> CreateWorkspaceAsync()
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.Workspace_Create", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Name", $"deleted-{Guid.NewGuid():N}"[..30]);
        cmd.Parameters.AddWithValue("@Description", "deleted-test");
        cmd.Parameters.AddWithValue("@OwnerUserId", await CreateUserAsync());
        cmd.Parameters.AddWithValue("@TenantId", 1);
        var refId = cmd.Parameters.Add("@RefId", SqlDbType.UniqueIdentifier);
        refId.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        return await ScalarAsync<int>("SELECT Id FROM dbo.Workspaces WHERE RefId = @p0", (Guid)refId.Value);
    }

    private async Task<int> CreateUserAsync()
    {
        string unique = $"deleted-{Guid.NewGuid():N}";
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(
            """
            INSERT INTO dbo.Users (Username, Email, PasswordHash, IsActive)
            OUTPUT INSERTED.Id
            VALUES (@Username, @Email, 'hash', 1);
            """, conn);
        cmd.Parameters.AddWithValue("@Username", unique);
        cmd.Parameters.AddWithValue("@Email", $"{unique}@example.test");
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task DeleteAsync(int workspaceId, bool withEvent)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.Workspace_Delete", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Id", workspaceId);
        if (withEvent)
        {
            cmd.Parameters.AddWithValue("@OutboxMessageType", MessageType);
            cmd.Parameters.AddWithValue("@OutboxPayload", $"{{\"workspaceId\":{workspaceId}}}");
        }

        await cmd.ExecuteNonQueryAsync();
    }

    private Task<DateTime> UtcNowAsync() => ScalarAsync<DateTime>("SELECT SYSUTCDATETIME()");

    private async Task<T> ScalarAsync<T>(string sql, params object[] args)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < args.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        }

        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }
}

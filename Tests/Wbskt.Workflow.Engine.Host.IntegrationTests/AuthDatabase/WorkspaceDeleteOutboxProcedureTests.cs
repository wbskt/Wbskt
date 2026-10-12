using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.AuthDatabase;

/// <summary>
/// dbo.Workspace_Delete writes the event that retires the workspace's main-database rows in the same
/// transaction as the delete, so a delete that commits always has its announcement queued.
/// </summary>
[Collection("SqlEdge")]
public sealed class WorkspaceDeleteOutboxProcedureTests(AuthSqlFixture fixture)
{
    private const string Skipped = "SQL Server not reachable — skipping.";
    private const string MessageType = "Wbskt.Events.Auth.WorkspaceDeletedEvent";

    [SkippableFact]
    public async Task Deleting_a_workspace_queues_its_event()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = await CreateWorkspaceAsync();
        string payload = $"{{\"workspaceId\":{workspaceId},\"deletedByUserId\":1}}";

        await DeleteAsync(workspaceId, payload);

        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Workspaces WHERE Id = @p0", workspaceId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.OutboxMessages WHERE MessageType = @p0 AND Payload = @p1", MessageType, payload));
    }

    [SkippableFact]
    public async Task Deleting_a_workspace_that_is_already_gone_queues_nothing()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        string payload = $"{{\"workspaceId\":-1,\"marker\":\"{Guid.NewGuid():N}\"}}";

        await DeleteAsync(-1, payload);

        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.OutboxMessages WHERE Payload = @p0", payload));
    }

    [SkippableFact]
    public async Task A_sent_row_is_read_in_order_and_deleted()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int first = await CreateWorkspaceAsync();
        int second = await CreateWorkspaceAsync();
        await DeleteAsync(first, $"{{\"workspaceId\":{first}}}");
        await DeleteAsync(second, $"{{\"workspaceId\":{second}}}");

        var pending = await GetPendingAsync();
        long firstId = pending.Single(p => p.Payload == $"{{\"workspaceId\":{first}}}").Id;
        long secondId = pending.Single(p => p.Payload == $"{{\"workspaceId\":{second}}}").Id;
        Assert.True(firstId < secondId);

        await using (var conn = await OpenAsync())
        await using (var cmd = new SqlCommand("dbo.OutboxMessage_Delete", conn) { CommandType = CommandType.StoredProcedure })
        {
            cmd.Parameters.AddWithValue("@Id", firstId);
            await cmd.ExecuteNonQueryAsync();
        }

        Assert.DoesNotContain(await GetPendingAsync(), p => p.Id == firstId);
    }

    private async Task<int> CreateWorkspaceAsync()
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.Workspace_Create", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Name", $"outbox-{Guid.NewGuid():N}"[..30]);
        cmd.Parameters.AddWithValue("@Description", "outbox-test");
        cmd.Parameters.AddWithValue("@OwnerUserId", await CreateUserAsync());
        cmd.Parameters.AddWithValue("@TenantId", 1);
        var refId = cmd.Parameters.Add("@RefId", SqlDbType.UniqueIdentifier);
        refId.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();
        return await ScalarAsync<int>("SELECT Id FROM dbo.Workspaces WHERE RefId = @p0", (Guid)refId.Value);
    }

    private async Task<int> CreateUserAsync()
    {
        string unique = $"outbox-{Guid.NewGuid():N}";
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

    private async Task DeleteAsync(int workspaceId, string payload)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.Workspace_Delete", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Id", workspaceId);
        cmd.Parameters.AddWithValue("@OutboxMessageType", MessageType);
        cmd.Parameters.AddWithValue("@OutboxPayload", payload);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<List<(long Id, string Payload)>> GetPendingAsync()
    {
        var rows = new List<(long, string)>();
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.OutboxMessage_GetPending", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Take", 1000);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetInt64(reader.GetOrdinal("Id")), reader.GetString(reader.GetOrdinal("Payload"))));
        }

        return rows;
    }

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

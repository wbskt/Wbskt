using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Events.Abstractions;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// How event log entries reach the table and leave it: the batch insert (whose table-valued
/// parameter is matched to <c>dbo.EventLogTableType</c> by column position, so a misordered column
/// lands data in the wrong place or fails every flush) and the retention sweep.
/// </summary>
[Collection("SqlEdge")]
public sealed class EventLogStorageIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";
    private static readonly DateTime Cutoff = new(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [SkippableFact]
    public async Task A_flushed_batch_lands_every_attribute_in_its_own_column()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        int eventId = await ScalarAsync<int>("""
            IF NOT EXISTS (SELECT 1 FROM dbo.Events WHERE EventName = N'StorageTestEvent')
                INSERT INTO dbo.Events (EventName, EventCriticality) VALUES (N'StorageTestEvent', 1);
            SELECT Id FROM dbo.Events WHERE EventName = N'StorageTestEvent';
            """);
        var userRefId = Guid.NewGuid();
        var workflowRefId = Guid.NewGuid();
        var entry = new EventLogEntry(eventId, "{}", DateTime.UtcNow, workspaceId,
            WorkflowId: 77, WorkflowRefId: workflowRefId, UserId: 42, UserRefId: userRefId,
            Source: EventSource.Console, ClientAddress: "81.2.69.160", UserAgent: "Firefox on macOS");

        await Provider().InsertBatchAsync(EventLogTable.Build([entry]));

        Assert.Equal(42, await ScalarAsync<int>("SELECT UserId FROM dbo.EventLogs WHERE WorkspaceId = @p0", workspaceId));
        Assert.Equal(userRefId, await ScalarAsync<Guid>("SELECT UserRefId FROM dbo.EventLogs WHERE WorkspaceId = @p0", workspaceId));
        Assert.Equal(77, await ScalarAsync<int>("SELECT WorkflowId FROM dbo.EventLogs WHERE WorkspaceId = @p0", workspaceId));
        Assert.Equal(workflowRefId, await ScalarAsync<Guid>("SELECT WorkflowRefId FROM dbo.EventLogs WHERE WorkspaceId = @p0", workspaceId));
        Assert.Equal((byte)EventSource.Console, await ScalarAsync<byte>("SELECT Source FROM dbo.EventLogs WHERE WorkspaceId = @p0", workspaceId));
        Assert.Equal("81.2.69.160", await ScalarAsync<string>("SELECT ClientAddress FROM dbo.EventLogs WHERE WorkspaceId = @p0", workspaceId));
        Assert.Equal("Firefox on macOS", await ScalarAsync<string>("SELECT UserAgent FROM dbo.EventLogs WHERE WorkspaceId = @p0", workspaceId));
    }

    [SkippableFact]
    public async Task A_redelivered_message_is_logged_once()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        int eventId = await ScalarAsync<int>("""
            IF NOT EXISTS (SELECT 1 FROM dbo.Events WHERE EventName = N'StorageTestEvent')
                INSERT INTO dbo.Events (EventName, EventCriticality) VALUES (N'StorageTestEvent', 1);
            SELECT Id FROM dbo.Events WHERE EventName = N'StorageTestEvent';
            """);
        EventLogEntry Entry(Guid? messageId) => new(eventId, "{}", DateTime.UtcNow, workspaceId, MessageId: messageId);
        var saved = Guid.NewGuid();
        var fresh = Guid.NewGuid();

        await Provider().InsertBatchAsync(EventLogTable.Build([Entry(saved)]));
        // The same message again (a redelivery), twice within one batch, next to a new one and two
        // entries without an id, which are always written.
        await Provider().InsertBatchAsync(EventLogTable.Build([Entry(saved), Entry(fresh), Entry(fresh), Entry(null), Entry(null)]));

        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.EventLogs WHERE MessageId = @p0", saved));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.EventLogs WHERE MessageId = @p0", fresh));
        Assert.Equal(4, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.EventLogs WHERE WorkspaceId = @p0", workspaceId));
    }

    /// <summary>Rows are dated in 2000 so the shared database's other rows are never old enough to be swept here.</summary>
    [SkippableFact]
    public async Task Deletes_only_rows_older_than_the_cutoff_and_at_most_a_batch_at_a_time()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        await ExecAsync("""
            DECLARE @EventId INT = (SELECT TOP 1 Id FROM dbo.Events);
            IF @EventId IS NULL
            BEGIN
                INSERT INTO dbo.Events (EventName, EventCriticality) VALUES (N'RetentionTestEvent', 1);
                SET @EventId = SCOPE_IDENTITY();
            END
            INSERT INTO dbo.EventLogs (EventId, WorkspaceId, CreatedAt)
            VALUES (@EventId, @p0, '2000-01-01'), (@EventId, @p0, '2000-01-02'), (@EventId, @p0, '2000-01-03'),
                   (@EventId, @p0, '2000-01-04'), (@EventId, @p0, '2000-01-05'),
                   (@EventId, @p0, '2001-01-01'), (@EventId, @p0, SYSUTCDATETIME());
            """, workspaceId);

        var provider = Provider();
        Assert.Equal(3, await provider.DeleteBeforeAsync(Cutoff, batchSize: 3));
        Assert.Equal(2, await provider.DeleteBeforeAsync(Cutoff, batchSize: 3));
        Assert.Equal(0, await provider.DeleteBeforeAsync(Cutoff, batchSize: 3));

        // The row exactly at the cutoff and the current one stay.
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.EventLogs WHERE WorkspaceId = @p0", workspaceId));
    }

    [SkippableFact]
    public async Task A_sweep_limited_to_some_events_leaves_the_others()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        int traffic = await ScalarAsync<int>("""
            IF NOT EXISTS (SELECT 1 FROM dbo.Events WHERE EventName = N'TrafficTestEvent')
                INSERT INTO dbo.Events (EventName, EventCriticality) VALUES (N'TrafficTestEvent', 1);
            SELECT Id FROM dbo.Events WHERE EventName = N'TrafficTestEvent';
            """);
        int audit = await ScalarAsync<int>("""
            IF NOT EXISTS (SELECT 1 FROM dbo.Events WHERE EventName = N'AuditTestEvent')
                INSERT INTO dbo.Events (EventName, EventCriticality) VALUES (N'AuditTestEvent', 1);
            SELECT Id FROM dbo.Events WHERE EventName = N'AuditTestEvent';
            """);
        await ExecAsync("""
            INSERT INTO dbo.EventLogs (EventId, WorkspaceId, CreatedAt)
            VALUES (@p1, @p0, '2000-01-01'), (@p1, @p0, '2000-01-02'), (@p2, @p0, '2000-01-01'), (@p1, @p0, SYSUTCDATETIME());
            """, workspaceId, traffic, audit);

        Assert.Equal(2, await Provider().DeleteBeforeAsync(Cutoff, batchSize: 100, eventIds: [traffic]));

        // The old audit row and the recent traffic row are untouched.
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.EventLogs WHERE WorkspaceId = @p0 AND EventId = @p1", workspaceId, audit));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.EventLogs WHERE WorkspaceId = @p0 AND EventId = @p1", workspaceId, traffic));

        // The old audit row would otherwise be counted by the other sweep test in this class.
        await ExecAsync("DELETE FROM dbo.EventLogs WHERE WorkspaceId = @p0", workspaceId);
    }

    private IEventProvider Provider() =>
        new EventProvider(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString })
            .Build());

    private async Task ExecAsync(string sql, params object[] args)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < args.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        }

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<T> ScalarAsync<T>(string sql, params object[] args)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < args.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        }

        return (T)(await cmd.ExecuteScalarAsync())!;
    }
}

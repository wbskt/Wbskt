using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// <c>dbo.WorkflowDefinition_Delete</c> and the reads that honour it. A deleted workflow must stop
/// firing, vanish from every current-version read, refuse to be published again, and keep the
/// versions its past runs point at.
/// </summary>
[Collection("SqlEdge")]
public sealed class WorkflowDeletionIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";

    [SkippableFact]
    public async Task Deleting_stops_the_workflow_and_keeps_its_history()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var provider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        Guid refId = Guid.NewGuid();
        var v1 = await provider.InsertAsync(Row(refId, workspaceId), CancellationToken.None);
        var v2 = await provider.InsertAsync(Row(refId, workspaceId), CancellationToken.None);
        Guid triggerNodeId = Guid.NewGuid();
        await ExecAsync("""
            INSERT INTO dbo.TriggerRegistrations (WorkflowDefinitionId, WorkflowRefId, WorkflowVersion, TriggerNodeId, TriggerKind, TriggerKey)
            VALUES (@p0, @p1, 2, @p2, 'webhook', CONCAT('webhook:delete:', @p1));
            INSERT INTO dbo.ScheduledFires (TriggerNodeId, WorkflowDefinitionId, WorkflowRefId, CronOrInterval, NextFireAt)
            VALUES (@p2, @p0, @p1, '* * * * *', SYSUTCDATETIME());
            INSERT INTO dbo.PendingTriggerEvents (WorkflowRefId, TriggerNodeId, CorrelationKey, InboundEventJson)
            VALUES (@p1, @p2, 'k', '{}');
            INSERT INTO dbo.Runs (RefId, WorkflowDefinitionId, WorkflowRefId, WorkflowVersion, TriggerNodeId, Status, StartedAt, CompletedAt, CreditBudget)
            VALUES (NEWID(), @p3, @p1, 1, @p2, N'Completed', SYSUTCDATETIME(), SYSUTCDATETIME(), 0);
            """, v2.Id, refId, triggerNodeId, v1.Id);
        long runningRunId = Convert.ToInt64(await ScalarAsync("""
            INSERT INTO dbo.Runs (RefId, WorkflowDefinitionId, WorkflowRefId, WorkflowVersion, TriggerNodeId, Status, StartedAt, CreditBudget)
            OUTPUT INSERTED.Id VALUES (NEWID(), @p0, @p1, 2, @p2, N'Running', SYSUTCDATETIME(), 0);
            """, v2.Id, refId, triggerNodeId));

        var versions = await provider.GetVersionsAsync(refId, workspaceId, CancellationToken.None);
        Assert.Equal([(2, 1L), (1, 1L)], versions.Select(v => (v.Version, v.RunCount)));

        Assert.Null(await provider.DeleteAsync(refId, workspaceId + 1, 9, CancellationToken.None));
        var deletion = await provider.DeleteAsync(refId, workspaceId, 9, CancellationToken.None);

        Assert.NotNull(deletion);
        Assert.Equal([runningRunId], deletion!.ActiveRunIds);
        Assert.Equal([v1.Id, v2.Id], deletion.DefinitionIds.Order());
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.TriggerRegistrations WHERE WorkflowRefId = @p0", refId));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.ScheduledFires WHERE WorkflowRefId = @p0", refId));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.PendingTriggerEvents WHERE WorkflowRefId = @p0", refId));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.WorkflowDefinitions WHERE RefId = @p0 AND IsEnabled = 1", refId));

        // Gone from current reads and the version list, but the version a run points at still reads.
        Assert.Null(await provider.FindCurrentByRefIdAsync(refId, CancellationToken.None));
        Assert.Empty(await provider.GetVersionsAsync(refId, workspaceId, CancellationToken.None));
        Assert.Equal(refId, (await provider.FindRowByRefIdVersionAsync(refId, 1, CancellationToken.None))!.RefId);
        Assert.DoesNotContain(
            (await provider.GetAllSummariesAsync(workspaceId, 0, 100, CancellationToken.None)).Select(r => r.RefId),
            r => r == refId);

        // Deleting again does nothing, and the RefId cannot come back.
        Assert.Null(await provider.DeleteAsync(refId, workspaceId, 9, CancellationToken.None));
        var ex = await Assert.ThrowsAsync<SqlException>(() => provider.InsertAsync(Row(refId, workspaceId), CancellationToken.None));
        Assert.Equal(50022, ex.Number);
    }

    // ---------------------------------------------------------------- helpers

    private static WorkflowDefinitionRow Row(Guid refId, int workspaceId) => new()
    {
        Id = 0,
        RefId = refId,
        Version = 0,
        WorkspaceId = workspaceId,
        Name = "delete-test",
        Description = null,
        IsEnabled = true,
        DefinitionJson = """{"nodes":[],"edges":[]}""",
        PublishedBy = 1,
        CreatedAt = DateTime.UtcNow
    };

    private async Task<int> CountAsync(string sql, params object[] args) => Convert.ToInt32(await ScalarAsync(sql, args));

    private Task ExecAsync(string sql, params object[] args) => RunAsync(sql, args, scalar: false);

    private Task<object?> ScalarAsync(string sql, params object[] args) => RunAsync(sql, args, scalar: true);

    private async Task<object?> RunAsync(string sql, object[] args, bool scalar)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < args.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        }

        return scalar ? await cmd.ExecuteScalarAsync() : await cmd.ExecuteNonQueryAsync();
    }
}

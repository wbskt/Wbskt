using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// The stats procedures work out p50 and p95 from one sort instead of PERCENTILE_CONT ... OVER () (#1116).
/// They must still give PERCENTILE_CONT's answer, over completed runs only.
/// </summary>
[Collection("SqlEdge")]
public sealed class RunStatsPercentileIntegrationTests(SqlEdgeFixture fixture)
{
    private const int WorkspaceId = 1;

    // Runs.CreatedAt is the server's clock, so the window reaches a minute past now.
    private static DateTime WindowStart() => DateTime.UtcNow.AddHours(-1);
    private static DateTime WindowEnd() => DateTime.UtcNow.AddMinutes(1);

    [SkippableFact]
    public async Task Workflow_percentiles_interpolate_between_ranks_over_completed_runs()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        WorkflowDefinitionRow wd = await CreateWorkflowAsync();
        foreach (int durationMs in new[] { 70, 10, 1000, 35, 7, 200, 20 })
        {
            await CreateRunAsync(wd, durationMs);
        }

        // Still running: counted, but not in the percentiles.
        await CreateRunAsync(wd, durationMs: null);

        RunStatsRow stats = await ProviderFactory.Run(fixture.ConnectionString).GetStatsAsync(wd.RefId, WindowStart(), WindowEnd(), CancellationToken.None);

        // Sorted: 7, 10, 20, 35, 70, 200, 1000. p50 sits on rank 3; p95 on rank 5.7, 70% of the way from 200 to 1000.
        stats.TotalRuns.Should().Be(8);
        stats.P50DurationMs.Should().BeApproximately(35, 0.001);
        stats.P95DurationMs.Should().BeApproximately(760, 0.001);
        (double p50, double p95) = await ReferencePercentilesAsync(wd.RefId, null);
        p50.Should().BeApproximately(stats.P50DurationMs, 0.001);
        p95.Should().BeApproximately(stats.P95DurationMs, 0.001);
    }

    [SkippableFact]
    public async Task Workflow_percentiles_match_percentile_cont_for_an_even_count_and_ties()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        WorkflowDefinitionRow wd = await CreateWorkflowAsync();
        foreach (int durationMs in new[] { 5, 5, 5, 40, 40, 90, 91, 300, 301, 1200 })
        {
            await CreateRunAsync(wd, durationMs);
        }

        RunStatsRow stats = await ProviderFactory.Run(fixture.ConnectionString).GetStatsAsync(wd.RefId, WindowStart(), WindowEnd(), CancellationToken.None);
        (double p50, double p95) = await ReferencePercentilesAsync(wd.RefId, null);

        stats.P50DurationMs.Should().BeApproximately(p50, 0.001);
        stats.P95DurationMs.Should().BeApproximately(p95, 0.001);
    }

    [SkippableFact]
    public async Task Workflow_percentiles_are_zero_when_no_run_has_completed()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        WorkflowDefinitionRow wd = await CreateWorkflowAsync();
        await CreateRunAsync(wd, durationMs: null);

        RunStatsRow stats = await ProviderFactory.Run(fixture.ConnectionString).GetStatsAsync(wd.RefId, WindowStart(), WindowEnd(), CancellationToken.None);

        stats.TotalRuns.Should().Be(1);
        stats.P50DurationMs.Should().Be(0);
        stats.P95DurationMs.Should().Be(0);
    }

    [SkippableFact]
    public async Task Workspace_percentiles_match_percentile_cont()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        WorkflowDefinitionRow wd = await CreateWorkflowAsync();
        foreach (int durationMs in new[] { 15, 250, 3, 3, 980, 64 })
        {
            await CreateRunAsync(wd, durationMs);
        }

        DateTime from = WindowStart();
        DateTime to = WindowEnd();
        RunStatsRow stats = await ProviderFactory.Run(fixture.ConnectionString).GetWorkspaceStatsAsync(WorkspaceId, from, to, CancellationToken.None);
        (double p50, double p95) = await ReferencePercentilesAsync(null, (from, to));

        stats.P50DurationMs.Should().BeApproximately(p50, 0.001);
        stats.P95DurationMs.Should().BeApproximately(p95, 0.001);
    }

    private async Task<WorkflowDefinitionRow> CreateWorkflowAsync()
    {
        return await ProviderFactory.WorkflowDefinition(fixture.ConnectionString).InsertAsync(new WorkflowDefinitionRow
        {
            Id = 0, RefId = Guid.NewGuid(), Version = 0, WorkspaceId = WorkspaceId,
            Name = $"IT-WD-{Guid.NewGuid():N}",
            Description = null,
            DefinitionJson = """{"nodes":[],"edges":[]}""",
            PublishedBy = 1, CreatedAt = DateTime.UtcNow
        }, CancellationToken.None).LiveAsync(fixture.ConnectionString);
    }

    /// <summary>A run in <paramref name="wd"/>, Succeeded after <paramref name="durationMs"/>, or still Running when null.</summary>
    private async Task CreateRunAsync(WorkflowDefinitionRow wd, int? durationMs)
    {
        RunRow run = await ActiveRunSeeder.StartWithNoLiveWorkAsync(fixture.ConnectionString, new RunRow
        {
            Id = 0, RefId = Guid.NewGuid(),
            WorkflowDefinitionId = wd.Id,
            WorkflowRefId = wd.RefId,
            WorkflowVersion = wd.Version,
            TriggerNodeId = Guid.NewGuid(),
            CorrelationKey = null,
            Status = "Running",
            StartedAt = DateTime.UtcNow,
            CompletedAt = null,
            CancellationRequestedAt = null,
            CancellationReason = null,
            CreditBudget = 0m,
            CreatedAt = DateTime.UtcNow
        });

        if (durationMs is null)
        {
            return;
        }

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            UPDATE dbo.Runs
            SET Status = N'Succeeded',
                CompletedAt = DATEADD(MILLISECOND, @DurationMs, StartedAt)
            WHERE Id = @RunId;
            """, connection);
        command.Parameters.AddWithValue("@DurationMs", durationMs.Value);
        command.Parameters.AddWithValue("@RunId", run.Id);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>PERCENTILE_CONT over the same runs the procedures read: the answer they must keep giving.</summary>
    private async Task<(double P50, double P95)> ReferencePercentilesAsync(Guid? workflowRefId, (DateTime From, DateTime To)? window)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            SELECT DISTINCT
                PERCENTILE_CONT(0.50) WITHIN GROUP (ORDER BY DurationMs) OVER (),
                PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY DurationMs) OVER ()
            FROM dbo.RunsInWindow(@WorkspaceId, @WorkflowRefId, @FromUtc, @ToUtc)
            WHERE DurationMs IS NOT NULL;
            """, connection);
        (DateTime from, DateTime to) = window ?? (WindowStart(), WindowEnd());
        command.Parameters.AddWithValue("@WorkspaceId", workflowRefId is null ? WorkspaceId : DBNull.Value);
        command.Parameters.AddWithValue("@WorkflowRefId", workflowRefId is { } refId ? refId : DBNull.Value);
        command.Parameters.Add("@FromUtc", System.Data.SqlDbType.DateTime2).Value = from;
        command.Parameters.Add("@ToUtc", System.Data.SqlDbType.DateTime2).Value = to;

        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetDouble(0), reader.GetDouble(1));
    }
}

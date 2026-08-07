using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// Task 13.5 — HistoryEvent TVP: verifies batch appends via HistoryEventTableType TVP
/// produce monotonically increasing HistoryEventIds.
/// </summary>
[Collection("SqlEdge")]
public sealed class HistoryEventTvpIntegrationTests(SqlEdgeFixture fixture)
{
    private async Task<int> CreateRunAsync()
    {
        var wdProvider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        WorkflowDefinitionRow wd = await wdProvider.InsertAsync(new WorkflowDefinitionRow
        {
            Id = 0, RefId = Guid.NewGuid(), Version = 0, WorkspaceId = 1,
            Name = $"IT-WD-HE-{Guid.NewGuid():N}",
            Description = null, IsEnabled = true,
            DefinitionJson = """{"nodes":[],"edges":[]}""",
            PublishedBy = 1, CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);

        var runProvider = ProviderFactory.Run(fixture.ConnectionString);
        RunRow run = await runProvider.CreateAsync(new RunRow
        {
            Id = 0, RefId = Guid.NewGuid(),
            WorkflowDefinitionId = wd.Id, WorkflowRefId = wd.RefId, WorkflowVersion = wd.Version,
            TriggerNodeId = Guid.NewGuid(), CorrelationKey = null, Status = "Running",
            StartedAt = DateTime.UtcNow, CompletedAt = null, CancellationRequestedAt = null,
            CancellationReason = null, CreditBudget = 0m, CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);

        return run.Id;
    }

    private static List<HistoryEventRow> BuildBatch(int runId, int count)
    {
        var now = DateTime.UtcNow;
        return Enumerable.Range(0, count).Select(i => new HistoryEventRow
        {
            HistoryEventId = 0,
            RunId = runId,
            BranchRefId = Guid.NewGuid(),
            NodeId = Guid.NewGuid(),
            EventKind = "NodeStarted",
            Severity = "Info",
            PayloadJson = null,
            Timestamp = now.AddMilliseconds(i)
        }).ToList();
    }

    [SkippableFact]
    public async Task AppendBatch_inserts_all_events()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        int runId = await CreateRunAsync();
        var provider = ProviderFactory.HistoryEvent(fixture.ConnectionString);

        const int count = 100;
        await provider.InsertBatchAsync(BuildBatch(runId, count), CancellationToken.None);

        // Verify row count via direct SQL
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM dbo.HistoryEvents WHERE RunId = @RunId";
        cmd.Parameters.AddWithValue("@RunId", runId);
        var result = await cmd.ExecuteScalarAsync();

        Convert.ToInt32(result).Should().Be(count);
    }

    [SkippableFact]
    public async Task AppendBatch_assigns_monotone_HistoryEventIds()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        int runId = await CreateRunAsync();
        var provider = ProviderFactory.HistoryEvent(fixture.ConnectionString);

        await provider.InsertBatchAsync(BuildBatch(runId, 10), CancellationToken.None);

        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT HistoryEventId FROM dbo.HistoryEvents
            WHERE RunId = @RunId
            ORDER BY HistoryEventId ASC
            """;
        cmd.Parameters.AddWithValue("@RunId", runId);

        var ids = new List<long>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetInt64(0));
        }

        ids.Should().HaveCount(10);
        for (int i = 1; i < ids.Count; i++)
        {
            ids[i].Should().BeGreaterThan(ids[i - 1], "HistoryEventIds should be strictly monotone");
        }
    }

    [SkippableFact]
    public async Task AppendBatch_multiple_calls_continue_monotone_sequence()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        int runId = await CreateRunAsync();
        var provider = ProviderFactory.HistoryEvent(fixture.ConnectionString);

        await provider.InsertBatchAsync(BuildBatch(runId, 5), CancellationToken.None);
        await provider.InsertBatchAsync(BuildBatch(runId, 5), CancellationToken.None);

        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT HistoryEventId FROM dbo.HistoryEvents
            WHERE RunId = @RunId
            ORDER BY HistoryEventId ASC
            """;
        cmd.Parameters.AddWithValue("@RunId", runId);

        var ids = new List<long>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetInt64(0));
        }

        ids.Should().HaveCount(10);
        ids.Should().BeInAscendingOrder();
    }

    [SkippableFact]
    public async Task AppendBatch_concurrent_for_same_RunId_does_not_produce_duplicate_HistoryEventIds()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        int runId = await CreateRunAsync();
        const int concurrency = 5;
        const int batchSize = 10;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, concurrency),
            new ParallelOptions { MaxDegreeOfParallelism = concurrency },
            async (_, ct) =>
            {
                var p = ProviderFactory.HistoryEvent(fixture.ConnectionString);
                await p.InsertBatchAsync(BuildBatch(runId, batchSize), ct);
            });

        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) AS Total,
                   COUNT(DISTINCT HistoryEventId) AS Distinct
            FROM dbo.HistoryEvents
            WHERE RunId = @RunId
            """;
        cmd.Parameters.AddWithValue("@RunId", runId);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        int total = reader.GetInt32(reader.GetOrdinal("Total"));
        int distinct = reader.GetInt32(reader.GetOrdinal("Distinct"));

        total.Should().Be(concurrency * batchSize);
        distinct.Should().Be(total, "HistoryEventIds must be globally unique (IDENTITY)");
    }
}

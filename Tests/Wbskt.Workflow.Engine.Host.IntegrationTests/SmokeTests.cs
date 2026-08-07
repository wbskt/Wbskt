using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests;

/// <summary>Task 13.1 — Smoke tests: SQL Edge reachable + workflow schema deployed.</summary>
[Collection("SqlEdge")]
public sealed class SmokeTests(SqlEdgeFixture fixture)
{
    [SkippableFact]
    public async Task Sql_edge_is_reachable()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        var result = await cmd.ExecuteScalarAsync();

        result.Should().Be(1);
    }

    [SkippableFact]
    public async Task Schema_deployed_workflow_tables_exist()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var expectedTables = new[]
        {
            "WorkflowDefinitions", "Runs", "RunCounters", "Branches",
            "Bookmarks", "HistoryEvents", "SharedVariables",
            "TriggerRegistrations", "PendingTriggerEvents",
            "ScheduledFires", "IdempotencyKeys"
        };

        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        foreach (string table in expectedTables)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT OBJECT_ID(N'dbo.{table}', N'U')";
            var result = await cmd.ExecuteScalarAsync();

            result.Should().NotBe(DBNull.Value, $"table dbo.{table} should exist");
            result.Should().NotBeNull($"table dbo.{table} should exist");
        }
    }

    [SkippableFact]
    public async Task Schema_deployed_stored_procedures_exist()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        var expectedSprocs = new[]
        {
            "WorkflowDefinition_Publish",
            "WorkflowDefinition_GetBy_RefId_Version",
            "WorkflowDefinition_GetLatestVersion_By_RefId",
            "Run_Create",
            "Run_SetTerminal",
            "RunCounters_IncrementActiveBranches",
            "RunCounters_DecrementActiveBranches",
            "Branch_Upsert",
            "Branch_GetBy_RefId",
            "Bookmark_Create",
            "Bookmark_GetDue",
            "HistoryEvent_InsertBatch",
            "SharedVariable_Initialize",
            "SharedVariable_CompareAndSet",
            "PendingTriggerEvent_Enqueue",
            "PendingTriggerEvent_DequeueNext"
        };

        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        foreach (string proc in expectedSprocs)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT OBJECT_ID(N'dbo.{proc}', N'P')";
            var result = await cmd.ExecuteScalarAsync();

            result.Should().NotBe(DBNull.Value, $"stored procedure dbo.{proc} should exist");
            result.Should().NotBeNull($"stored procedure dbo.{proc} should exist");
        }
    }
}

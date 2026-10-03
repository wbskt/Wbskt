using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// Procedure-level guards against replays and races: a branch contributing to a join twice, a run
/// counter decremented below zero, a first publish of one workflow from two workspaces, and a device
/// growing its state variables without bound.
/// </summary>
[Collection("SqlEdge")]
public sealed class EngineCorrectnessIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";

    [SkippableFact]
    public async Task A_replayed_join_contribution_is_counted_once_and_never_continues()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var joinToken = Guid.NewGuid();
        await ProcAsync("dbo.JoinAggregator_Initialize", ("@JoinToken", joinToken), ("@RunId", Random.Shared.Next(1_000_000, int.MaxValue)), ("@ExpectedCount", 2));

        var first = await ContributeAsync(joinToken, branchId: 1);
        var replay = await ContributeAsync(joinToken, branchId: 1);

        Assert.Equal((false, 1), first);
        Assert.Equal((false, 1), replay);

        // Only the second member's arrival closes the 'All' cohort.
        Assert.Equal((true, 2), await ContributeAsync(joinToken, branchId: 2));
        Assert.Equal((false, 2), await ContributeAsync(joinToken, branchId: 2));
    }

    [SkippableFact]
    public async Task The_active_branch_count_never_goes_negative()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var workflowRefId = Guid.NewGuid();
        await PublishAsync(workflowRefId, Random.Shared.Next(1_000_000, int.MaxValue));
        int runId = await ScalarAsync<int>("""
            INSERT INTO dbo.Runs (RefId, WorkflowDefinitionId, WorkflowRefId, WorkflowVersion, TriggerNodeId, Status, StartedAt, CreditBudget)
            OUTPUT INSERTED.Id
            SELECT NEWID(), Id, RefId, Version, NEWID(), N'Running', SYSUTCDATETIME(), 0
            FROM dbo.WorkflowDefinitions WHERE RefId = @p0;
            """, workflowRefId);
        await ExecAsync("INSERT INTO dbo.RunCounters (RunId, ActiveBranchCount) VALUES (@p0, 1);", runId);

        await ProcAsync("dbo.RunCounters_DecrementActiveBranches", ("@RunId", runId), ("@Delta", 1));
        await ProcAsync("dbo.RunCounters_DecrementActiveBranches", ("@RunId", runId), ("@Delta", 1));

        Assert.Equal(0, await ScalarAsync<int>("SELECT ActiveBranchCount FROM dbo.RunCounters WHERE RunId = @p0", runId));
    }

    [SkippableFact]
    public async Task A_workflow_cannot_be_published_into_a_second_workspace()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var refId = Guid.NewGuid();
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);

        await PublishAsync(refId, workspaceId);

        var ex = await Assert.ThrowsAsync<SqlException>(() => PublishAsync(refId, workspaceId - 1));
        Assert.Equal(50021, ex.Number);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.WorkflowDefinitions WHERE RefId = @p0", refId));

        // The owning workspace still publishes new versions.
        await PublishAsync(refId, workspaceId);
        Assert.Equal(2, await ScalarAsync<int>("SELECT MAX(Version) FROM dbo.WorkflowDefinitions WHERE RefId = @p0", refId));
    }

    [SkippableFact]
    public async Task A_client_at_its_variable_cap_can_update_but_not_add()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        int clientId = await ScalarAsync<int>("""
            DECLARE @PolicyId INT;
            INSERT INTO dbo.RegistrationPolicies (WorkspaceId, Pin, Name) VALUES (@p0, LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), '-', ''), 20), N'cap');
            SET @PolicyId = SCOPE_IDENTITY();
            INSERT INTO dbo.Clients (WorkspaceId, PolicyId, Name, SecretHash) OUTPUT INSERTED.Id
            VALUES (@p0, @PolicyId, N'cap', 0x00);
            """, workspaceId);

        Assert.Equal((true, (string?)null), await UpsertAsync(clientId, "a", "1", maxPerClient: 2));
        Assert.Equal((true, (string?)null), await UpsertAsync(clientId, "b", "1", maxPerClient: 2));
        Assert.Equal((false, (string?)null), await UpsertAsync(clientId, "c", "1", maxPerClient: 2));
        Assert.Equal((true, "1"), await UpsertAsync(clientId, "a", "2", maxPerClient: 2));

        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ClientStateVariables WHERE ClientId = @p0", clientId));
    }

    // ---------------------------------------------------------------- helpers

    private async Task<(bool ShouldContinue, int Contributed)> ContributeAsync(Guid joinToken, long branchId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.JoinAggregator_Contribute", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@JoinToken", joinToken);
        cmd.Parameters.AddWithValue("@BranchId", branchId);
        cmd.Parameters.AddWithValue("@Outcome", "succeeded");
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetBoolean(reader.GetOrdinal("ShouldContinue")), reader.GetInt32(reader.GetOrdinal("ContributedCount")));
    }

    private async Task<(bool Stored, string? Old)> UpsertAsync(int clientId, string name, string valueJson, int maxPerClient)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.ClientStateVariable_Upsert", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@ClientId", clientId);
        cmd.Parameters.AddWithValue("@Name", name);
        cmd.Parameters.AddWithValue("@DataType", "number");
        cmd.Parameters.AddWithValue("@ValueJson", valueJson);
        cmd.Parameters.AddWithValue("@MaxPerClient", maxPerClient);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetBoolean(reader.GetOrdinal("Stored")),
            reader.IsDBNull(reader.GetOrdinal("OldValueJson")) ? null : reader.GetString(reader.GetOrdinal("OldValueJson")));
    }

    private Task PublishAsync(Guid refId, int workspaceId) =>
        ProcAsync("dbo.WorkflowDefinition_Publish",
            ("@RefId", refId), ("@WorkspaceId", workspaceId), ("@Name", "wf"), ("@Description", ""),
            ("@IsEnabled", true), ("@DefinitionJson", "{}"), ("@PublishedBy", 1));

    private async Task ProcAsync(string procedure, params (string Name, object Value)[] parameters)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(procedure, conn) { CommandType = CommandType.StoredProcedure };
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.NextResultAsync())
        {
        }
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

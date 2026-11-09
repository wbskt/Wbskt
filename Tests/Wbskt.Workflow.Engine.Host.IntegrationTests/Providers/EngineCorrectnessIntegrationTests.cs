using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Models.Billing;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// Procedure-level guards against replays and races: a branch contributing to a join twice, a run
/// counter decremented below zero, a first publish of one workflow from two workspaces, and a client
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
    public async Task A_declined_member_abandons_an_All_join_and_no_later_arrival_continues()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var joinToken = Guid.NewGuid();
        await ProcAsync("dbo.JoinAggregator_Initialize", ("@JoinToken", joinToken), ("@RunId", Random.Shared.Next(1_000_000, int.MaxValue)), ("@ExpectedCount", 2), ("@Mode", "All"), ("@JoinNodeId", Guid.NewGuid()));

        var declined = await ContributeWithAbandonedAsync(joinToken, branchId: 1, "declined");
        var replay = await ContributeWithAbandonedAsync(joinToken, branchId: 1, "declined");
        var arrival = await ContributeWithAbandonedAsync(joinToken, branchId: 2, "succeeded");

        Assert.Equal((false, true), declined);
        Assert.Equal((false, false), replay);
        Assert.Equal((false, false), arrival);
    }

    [SkippableFact]
    public async Task A_declined_member_of_an_Any_join_lets_the_next_arrival_continue()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var joinToken = Guid.NewGuid();
        await ProcAsync("dbo.JoinAggregator_Initialize", ("@JoinToken", joinToken), ("@RunId", Random.Shared.Next(1_000_000, int.MaxValue)), ("@ExpectedCount", 2), ("@Mode", "Any"), ("@JoinNodeId", Guid.NewGuid()));

        Assert.Equal((false, false), await ContributeWithAbandonedAsync(joinToken, branchId: 1, "declined"));
        Assert.Equal((true, false), await ContributeWithAbandonedAsync(joinToken, branchId: 2, "succeeded"));
    }

    [SkippableFact]
    public async Task A_declined_member_of_a_cohort_without_a_Join_abandons_nothing()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var joinToken = Guid.NewGuid();
        await ProcAsync("dbo.JoinAggregator_Initialize", ("@JoinToken", joinToken), ("@RunId", Random.Shared.Next(1_000_000, int.MaxValue)), ("@ExpectedCount", 2));

        Assert.Equal((false, false), await ContributeWithAbandonedAsync(joinToken, branchId: 1, "declined"));
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
    public async Task Registering_a_schedule_twice_keeps_one_row()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var refId = Guid.NewGuid();
        await PublishAsync(refId, Random.Shared.Next(1_000_000, int.MaxValue));
        int definitionId = await ScalarAsync<int>("SELECT Id FROM dbo.WorkflowDefinitions WHERE RefId = @p0", refId);
        var triggerNodeId = Guid.NewGuid();

        int first = await InsertScheduleAsync(definitionId, refId, triggerNodeId);
        int second = await InsertScheduleAsync(definitionId, refId, triggerNodeId);

        Assert.Equal(first, second);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ScheduledFires WHERE WorkflowDefinitionId = @p0", definitionId));
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

        var clients = new ClientProvider(ProviderFactory.BuildConfiguration(fixture.ConnectionString));

        // One report past the cap: the earlier new names take the places, in the order reported.
        Assert.Equal(
            [new StateVariableUpsert("b", true, null), new StateVariableUpsert("a", true, null), new StateVariableUpsert("c", false, null)],
            await clients.ReportStateVariablesAsync(clientId, [Variable("b", "1"), Variable("a", "1"), Variable("c", "1")], maxPerClient: 2));
        // At the cap, a known name still updates, case-insensitively, and returns what it replaced.
        Assert.Equal(
            [new StateVariableUpsert("A", true, "1"), new StateVariableUpsert("d", false, null)],
            await clients.ReportStateVariablesAsync(clientId, [Variable("A", "2"), Variable("d", "1")], maxPerClient: 2));

        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ClientStateVariables WHERE ClientId = @p0", clientId));
    }

    /// <summary>
    /// <c>dbo.Run_DeleteRetired</c> removes a long-finished run and everything that hangs off it,
    /// and leaves a run still going and one finished after the cutoff. Runs complete in 2000 and the
    /// cutoff is 2001, so the shared database's other runs are never swept here.
    /// </summary>
    [SkippableFact]
    public async Task Retiring_runs_removes_old_completed_runs_and_their_rows_only()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var workflowRefId = Guid.NewGuid();
        await PublishAsync(workflowRefId, Random.Shared.Next(1_000_000, int.MaxValue));

        int oldRun = await InsertRunAsync(workflowRefId, "Succeeded", "2000-01-01");
        int recentRun = await InsertRunAsync(workflowRefId, "Succeeded", "2001-06-01");
        int liveRun = await InsertRunAsync(workflowRefId, "Running", null);
        foreach (int runId in new[] { oldRun, recentRun, liveRun })
        {
            await ExecAsync("""
                INSERT INTO dbo.RunCounters (RunId) VALUES (@p0);
                INSERT INTO dbo.Branches (RefId, RunId, NodeId, Status) VALUES (NEWID(), @p0, NEWID(), N'Completed');
                INSERT INTO dbo.HistoryEvents (RunId, NodeId, EventKind, Severity, PayloadJson, Timestamp)
                VALUES (@p0, NEWID(), N'RunStarted', N'Error', N'{}', '2000-01-01');
                """, runId);
        }

        await ProviderFactory.RunRetention(fixture.ConnectionString)
            .DeleteRetiredRunsAsync(new DateTime(2001, 1, 1), [new RunHistoryRetention(PlanCode.Free, 36_500)], new DateTime(2001, 1, 1), 100, CancellationToken.None);

        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Runs WHERE Id = @p0", oldRun));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Branches WHERE RunId = @p0", oldRun));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.HistoryEvents WHERE RunId = @p0", oldRun));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RunCounters WHERE RunId = @p0", oldRun));
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Runs WHERE Id IN (@p0, @p1)", recentRun, liveRun));
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Branches WHERE RunId IN (@p0, @p1)", recentRun, liveRun));
    }

    /// <summary>
    /// A finished run goes once its workspace's plan stops keeping it: a tenant's <c>runHistoryDays</c>
    /// override wins over its plan, a retention held after a downgrade wins while it runs, and a workspace
    /// with no copy of its account loses nothing. "Now" is 2001-01-01 and the runs finished 12 days
    /// before, so the shared database's other runs are never swept here.
    /// </summary>
    [SkippableFact]
    public async Task Retiring_runs_keeps_each_for_its_workspace_plans_days()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var now = new DateTime(2001, 1, 1);
        int free = await RunInWorkspaceAsync(plan: "Free", overrides: null);
        int noCopy = await RunInWorkspaceAsync(plan: null, overrides: null);
        int pro = await RunInWorkspaceAsync(plan: "Pro", overrides: null);
        int overridden = await RunInWorkspaceAsync(plan: "Pro", overrides: """{"runHistoryDays":10}""");
        int held = await RunInWorkspaceAsync(plan: "Free", overrides: null, heldDays: 30, heldUntil: now.AddDays(1));
        int lapsed = await RunInWorkspaceAsync(plan: "Free", overrides: null, heldDays: 30, heldUntil: now.AddDays(-1));

        await ProviderFactory.RunRetention(fixture.ConnectionString).DeleteRetiredRunsAsync(
            now,
            [new RunHistoryRetention(PlanCode.Free, 7), new RunHistoryRetention(PlanCode.Maker, 14), new RunHistoryRetention(PlanCode.Pro, 30), new RunHistoryRetention(PlanCode.Business, 30)],
            new DateTime(1990, 1, 1),
            100,
            CancellationToken.None);

        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Runs WHERE Id = @p0", free));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Runs WHERE Id = @p0", pro));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Runs WHERE Id = @p0", overridden));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Runs WHERE Id = @p0", noCopy));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Runs WHERE Id = @p0", held));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Runs WHERE Id = @p0", lapsed));
    }

    private async Task<int> RunInWorkspaceAsync(string? plan, string? overrides, int? heldDays = null, DateTime? heldUntil = null)
    {
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        if (plan is not null)
        {
            await ExecAsync("""
                INSERT INTO dbo.WorkspaceAccounts (WorkspaceId, TenantId, TenantRefId, PlanCode, LimitOverrides, PlanRevision, HeldRunHistoryDays, RetentionHeldUntil)
                VALUES (@p0, @p0, NEWID(), @p1, @p2, 1, @p3, @p4);
                """, workspaceId, plan, (object?)overrides ?? DBNull.Value, (object?)heldDays ?? DBNull.Value, (object?)heldUntil ?? DBNull.Value);
        }

        var workflowRefId = Guid.NewGuid();
        await PublishAsync(workflowRefId, workspaceId);
        return await InsertRunAsync(workflowRefId, "Succeeded", "2000-12-20");
    }

    private Task<int> InsertRunAsync(Guid workflowRefId, string status, string? completedAt) =>
        ScalarAsync<int>("""
            INSERT INTO dbo.Runs (RefId, WorkflowDefinitionId, WorkflowRefId, WorkflowVersion, TriggerNodeId, Status, StartedAt, CompletedAt, CreditBudget)
            OUTPUT INSERTED.Id
            SELECT NEWID(), Id, RefId, Version, NEWID(), @p1, '2000-01-01', CONVERT(DATETIME2(3), @p2), 0
            FROM dbo.WorkflowDefinitions WHERE RefId = @p0;
            """, workflowRefId, status, (object?)completedAt ?? DBNull.Value);

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

    private async Task<(bool ShouldContinue, bool Abandoned)> ContributeWithAbandonedAsync(Guid joinToken, long branchId, string outcome)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.JoinAggregator_Contribute", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@JoinToken", joinToken);
        cmd.Parameters.AddWithValue("@BranchId", branchId);
        cmd.Parameters.AddWithValue("@Outcome", outcome);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetBoolean(reader.GetOrdinal("ShouldContinue")), reader.GetBoolean(reader.GetOrdinal("Abandoned")));
    }

    private static StateVariableValue Variable(string name, string valueJson) => new(name, "number", valueJson);

    private async Task<int> InsertScheduleAsync(int definitionId, Guid workflowRefId, Guid triggerNodeId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.ScheduledFire_Insert", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@TriggerNodeId", triggerNodeId);
        cmd.Parameters.AddWithValue("@WorkflowDefinitionId", definitionId);
        cmd.Parameters.AddWithValue("@WorkflowRefId", workflowRefId);
        cmd.Parameters.AddWithValue("@CronOrInterval", "*/5 * * * *");
        cmd.Parameters.AddWithValue("@NextFireAt", DateTime.UtcNow);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return reader.GetInt32(reader.GetOrdinal("Id"));
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

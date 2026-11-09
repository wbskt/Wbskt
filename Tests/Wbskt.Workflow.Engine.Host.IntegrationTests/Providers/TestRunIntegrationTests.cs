using Microsoft.Data.SqlClient;
using Wbskt.Models.Billing;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// Test runs (#599): a test copy is invisible to every version lookup, one test run goes at a time per
/// workflow without holding up live runs, test runs share the workflow's numbers but stay out of its
/// stats, and retention removes test copies nothing points at.
/// </summary>
[Collection("SqlEdge")]
public sealed class TestRunIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";
    private const int TestRunActiveError = 50029;

    [SkippableFact]
    public async Task A_test_copy_is_not_a_version()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var definitions = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        int workspaceId = NewWorkspace();
        Guid refId = Guid.NewGuid();

        var v1 = await definitions.InsertAsync(Row(refId, workspaceId), CancellationToken.None);
        var copy = await definitions.InsertTestCopyAsync(Row(refId, workspaceId) with { Version = 1 }, CancellationToken.None);

        Assert.NotEqual(v1.Id, copy.Id);
        Assert.Equal(1, copy.Version);
        Assert.Equal(v1.Id, (await definitions.FindCurrentByRefIdAsync(refId, CancellationToken.None))!.Id);
        Assert.Equal(v1.Id, await definitions.FindByRefIdVersionAsync(refId, 1, CancellationToken.None));
        Assert.Single(await definitions.GetVersionsAsync(refId, workspaceId, CancellationToken.None));
        Assert.Single(await definitions.GetAllSummariesAsync(workspaceId, 0, 10, CancellationToken.None));
        Assert.Equal(2, (await definitions.InsertAsync(Row(refId, workspaceId), CancellationToken.None)).Version);

        // A never-published workflow's test copy is version 0, and still no version.
        Guid fresh = Guid.NewGuid();
        await definitions.InsertTestCopyAsync(Row(fresh, workspaceId), CancellationToken.None);
        Assert.Null(await definitions.FindCurrentByRefIdAsync(fresh, CancellationToken.None));
        Assert.Equal(1, (await definitions.InsertAsync(Row(fresh, workspaceId), CancellationToken.None)).Version);
    }

    [SkippableFact]
    public async Task One_test_run_goes_at_a_time_and_live_runs_are_not_held_up()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var definitions = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        var runs = ProviderFactory.Run(fixture.ConnectionString);
        int workspaceId = NewWorkspace();
        Guid refId = Guid.NewGuid();
        var live = await definitions.InsertAsync(Row(refId, workspaceId), CancellationToken.None);
        var copy = await definitions.InsertTestCopyAsync(Row(refId, workspaceId) with { Version = 1 }, CancellationToken.None);

        var first = await StartAsync(runs, copy, isTest: true);
        var refused = await Assert.ThrowsAsync<SqlException>(() => StartAsync(runs, copy, isTest: true));
        Assert.Equal(TestRunActiveError, refused.Number);
        Assert.Equal(first.Run.RefId, await runs.FindActiveTestRunAsync(refId, CancellationToken.None));

        var liveRun = await StartAsync(runs, live, isTest: false);
        Assert.False(liveRun.Run.IsTest);
        Assert.True(first.Run.IsTest);
        Assert.Equal(first.Run.Number + 1, liveRun.Run.Number);

        await runs.SetTerminalAsync(first.Run.Id, RunStatus.Succeeded, DateTime.UtcNow, CancellationToken.None);
        Assert.Null(await runs.FindActiveTestRunAsync(refId, CancellationToken.None));
        var second = await StartAsync(runs, copy, isTest: true);
        Assert.Equal(liveRun.Run.Number + 1, second.Run.Number);

        var tests = await runs.ListByWorkflowAsync(refId, null, true, 10, null, CancellationToken.None);
        Assert.Equal([second.Run.RefId, first.Run.RefId], tests.Select(r => r.RefId));
        var notTests = await runs.ListByWorkflowAsync(refId, null, false, 10, null, CancellationToken.None);
        Assert.Equal([liveRun.Run.RefId], notTests.Select(r => r.RefId));
        Assert.True((await runs.FindRowByRefIdAsync(second.Run.RefId, CancellationToken.None))!.IsTest);
    }

    [SkippableFact]
    public async Task A_test_runs_sub_workflows_are_test_runs_and_not_held_to_one_at_a_time()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var definitions = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        var runs = ProviderFactory.Run(fixture.ConnectionString);
        int workspaceId = NewWorkspace();
        var parentCopy = await definitions.InsertTestCopyAsync(Row(Guid.NewGuid(), workspaceId), CancellationToken.None);
        var child = await definitions.InsertAsync(Row(Guid.NewGuid(), workspaceId), CancellationToken.None);
        var parent = await StartAsync(runs, parentCopy, isTest: true);

        var first = await StartAsync(runs, child, isTest: false, parentRunRefId: parent.Run.RefId);
        var second = await StartAsync(runs, child, isTest: false, parentRunRefId: parent.Run.RefId);
        var live = await StartAsync(runs, child, isTest: false);

        Assert.True(first.Run.IsTest);
        Assert.True(second.Run.IsTest);
        Assert.False(live.Run.IsTest);
        Assert.True((await definitions.GetByIdAsync(parentCopy.Id, CancellationToken.None)).IsTestCopy);
        Assert.False((await definitions.GetByIdAsync(child.Id, CancellationToken.None)).IsTestCopy);
    }

    [SkippableFact]
    public async Task A_deleted_workflow_starts_no_test_run()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var definitions = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        var runs = ProviderFactory.Run(fixture.ConnectionString);
        int workspaceId = NewWorkspace();
        Guid refId = Guid.NewGuid();
        var copy = await definitions.InsertTestCopyAsync(Row(refId, workspaceId), CancellationToken.None);

        await using (var conn = new SqlConnection(fixture.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new SqlCommand("INSERT INTO dbo.WorkflowDeletions (RefId, WorkspaceId, DeletedBy) VALUES (@RefId, @WorkspaceId, 1)", conn);
            cmd.Parameters.AddWithValue("@RefId", refId);
            cmd.Parameters.AddWithValue("@WorkspaceId", workspaceId);
            await cmd.ExecuteNonQueryAsync();
        }

        var refused = await Assert.ThrowsAsync<SqlException>(() => StartAsync(runs, copy, isTest: true));
        Assert.Equal(50022, refused.Number);
    }

    [SkippableFact]
    public async Task Test_runs_stay_out_of_stats()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var definitions = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        var runs = ProviderFactory.Run(fixture.ConnectionString);
        int workspaceId = NewWorkspace();
        Guid refId = Guid.NewGuid();
        var live = await definitions.InsertAsync(Row(refId, workspaceId), CancellationToken.None);
        var copy = await definitions.InsertTestCopyAsync(Row(refId, workspaceId) with { Version = 1 }, CancellationToken.None);

        await StartAsync(runs, live, isTest: false);
        await StartAsync(runs, copy, isTest: true);

        DateTime from = DateTime.UtcNow.AddDays(-1), to = DateTime.UtcNow.AddDays(1);
        var days = await runs.GetDailyStatsAsync(refId, from, to, CancellationToken.None);
        Assert.Equal(1, days.Sum(d => d.TotalRuns));
        var workspaceDays = await runs.GetWorkspaceDailyStatsAsync(workspaceId, from, to, CancellationToken.None);
        Assert.Equal(1, workspaceDays.Sum(d => d.TotalRuns));
    }

    [SkippableFact]
    public async Task Retention_removes_test_copies_no_run_points_at()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var definitions = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        var runs = ProviderFactory.Run(fixture.ConnectionString);
        int workspaceId = NewWorkspace();
        Guid refId = Guid.NewGuid();
        var used = await definitions.InsertTestCopyAsync(Row(refId, workspaceId), CancellationToken.None);
        var unused = await definitions.InsertTestCopyAsync(Row(refId, workspaceId), CancellationToken.None);
        await StartAsync(runs, used, isTest: true);
        await BackdateAsync(used.Id, unused.Id);

        await ProviderFactory.RunRetention(fixture.ConnectionString).DeleteRetiredRunsAsync(new DateTime(2001, 1, 1), [new RunHistoryRetention(PlanCode.Free, 36_500)], new DateTime(2001, 1, 1), 100, CancellationToken.None);

        Assert.NotNull(await TryGetAsync(used.Id));
        Assert.Null(await TryGetAsync(unused.Id));
    }

    private async Task<WorkflowDefinitionRow?> TryGetAsync(int id)
    {
        try
        {
            return await ProviderFactory.WorkflowDefinition(fixture.ConnectionString).GetByIdAsync(id, CancellationToken.None);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private async Task BackdateAsync(params int[] ids)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand($"UPDATE dbo.WorkflowDefinitions SET CreatedAt = '2000-01-01' WHERE Id IN ({string.Join(",", ids)})", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static Task<RunStartRow> StartAsync(Wbskt.Workflow.Providers.RunProvider runs, WorkflowDefinitionRow definition, bool isTest, Guid? parentRunRefId = null)
    {
        DateTime now = DateTime.UtcNow;
        Guid triggerNodeId = Guid.NewGuid();
        var run = new RunRow
        {
            Id = 0, RefId = Guid.NewGuid(),
            WorkflowDefinitionId = definition.Id,
            WorkflowRefId = definition.RefId,
            WorkflowVersion = definition.Version,
            TriggerNodeId = triggerNodeId,
            CorrelationKey = isTest ? $"test:{Guid.NewGuid()}" : "it",
            Status = RunStatus.Running,
            StartedAt = now,
            CompletedAt = null,
            CancellationRequestedAt = null,
            CancellationReason = null,
            CreditBudget = 10m,
            CreatedAt = now,
            ParentRunRefId = parentRunRefId,
            IsTest = isTest
        };
        var branch = new BranchRow
        {
            Id = 0, RefId = Guid.NewGuid(), RunId = 0,
            ParentBranchId = null, ForkCohortId = null,
            NodeId = triggerNodeId,
            Status = BranchStatus.Active,
            PendingTakePort = null,
            LocalJson = """{"trigger":{}}""",
            LastOutputJson = null,
            CreatedAt = now, UpdatedAt = now,
            RowVersion = Array.Empty<byte>()
        };
        return runs.StartAsync(run, branch, null, CancellationToken.None);
    }

    private static int NewWorkspace() => Random.Shared.Next(1_000_000, int.MaxValue - 1);

    private static WorkflowDefinitionRow Row(Guid refId, int workspaceId) => new()
    {
        Id = 0,
        RefId = refId,
        Version = 0,
        WorkspaceId = workspaceId,
        Name = "test-run-test",
        Description = null,
        IsEnabled = true,
        DefinitionJson = """{"nodes":[],"edges":[]}""",
        PublishedBy = 1,
        CreatedAt = DateTime.UtcNow
    };
}

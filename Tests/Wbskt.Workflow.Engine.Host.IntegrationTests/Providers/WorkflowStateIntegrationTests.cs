using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;
using Wbskt.Workflow.Providers;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// The workflow state procedures are the one authority on which transitions are legal, so each one is
/// checked here against the real schema: what every action does from every state, that registrations,
/// schedules and queued events move with the state in one transaction, and that <c>dbo.Run_Start</c>
/// refuses a version that is not live.
/// </summary>
[Collection("SqlEdge")]
public sealed class WorkflowStateIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";
    private const int NotFound = 50040;
    private const int Deprecated = 50041;
    private const int NotDeprecated = 50042;
    private const int NoSuchVersion = 50043;
    private const int IntegrationMissing = 50023;
    private const int DeletedError = 50022;
    private const int NotLive = 50036;
    private const int User = 1;

    private static readonly Guid TriggerA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid TriggerB = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");

    private WorkflowStateProvider States => ProviderFactory.WorkflowState(fixture.ConnectionString);

    [SkippableFact]
    public async Task A_first_publish_is_disabled_and_registers_nothing()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (refId, workspaceId, _) = await PublishAsync(versions: 1);

        WorkflowStateRow state = (await States.FindAsync(refId, workspaceId, CancellationToken.None))!;

        Assert.Equal(WorkflowState.Disabled, state.State);
        Assert.Null(state.LiveVersion);
        Assert.Equal(1, state.LatestVersion);
        Assert.Equal(0, await RegistrationsAsync(refId));
    }

    [SkippableFact]
    public async Task Enabling_registers_the_plan_and_a_repeat_changes_nothing()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (refId, workspaceId, ids) = await PublishAsync(versions: 1);

        WorkflowEnableResult first = await EnableAsync(refId, workspaceId, 1, Plan(refId, TriggerA, withSchedule: true));
        WorkflowEnableResult again = await EnableAsync(refId, workspaceId, 1, Plan(refId, TriggerA, withSchedule: true));

        Assert.Equal(new WorkflowEnableResult(true, null, ids[0], 0), first);
        Assert.False(again.Changed);
        Assert.Equal(ids[0], again.PreviousLiveDefinitionId);
        Assert.Equal(1, await RegistrationsAsync(refId));
        Assert.Equal(1, await SchedulesAsync(refId));
        WorkflowStateRow state = (await States.FindAsync(refId, workspaceId, CancellationToken.None))!;
        Assert.Equal((WorkflowState.Enabled, 1, ids[0]), (state.State, state.LiveVersion, state.LiveDefinitionId));
    }

    [SkippableFact]
    public async Task Switching_the_live_version_replaces_its_registrations_and_keeps_only_events_for_triggers_it_still_has()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (refId, workspaceId, ids) = await PublishAsync(versions: 2);
        await EnableAsync(refId, workspaceId, 1, new TriggerPlan([Registration(refId, TriggerA), Registration(refId, TriggerB)], []));
        await QueueBehindRunAsync(refId, ids[0], TriggerA);
        await QueueBehindRunAsync(refId, ids[0], TriggerB);

        WorkflowEnableResult switched = await EnableAsync(refId, workspaceId, 2, Plan(refId, TriggerA));

        Assert.Equal(new WorkflowEnableResult(true, ids[0], ids[1], 1), switched);
        Assert.Equal([ids[1]], await RegisteredDefinitionsAsync(refId));
        Assert.Equal(1, await QueuedAsync(refId, TriggerA));
        Assert.Equal(0, await QueuedAsync(refId, TriggerB));
    }

    [SkippableFact]
    public async Task Any_published_version_can_be_made_live_including_an_older_one()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (refId, workspaceId, ids) = await PublishAsync(versions: 3);
        await EnableAsync(refId, workspaceId, 3, Plan(refId, TriggerA));

        await EnableAsync(refId, workspaceId, 1, Plan(refId, TriggerA));

        WorkflowStateRow state = (await States.FindAsync(refId, workspaceId, CancellationToken.None))!;
        Assert.Equal((1, ids[0], 3), (state.LiveVersion!.Value, state.LiveDefinitionId!.Value, state.LatestVersion));
        Assert.Equal([ids[0]], await RegisteredDefinitionsAsync(refId));
    }

    [SkippableFact]
    public async Task Publishing_a_new_version_leaves_the_live_one_live()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (refId, workspaceId, ids) = await PublishAsync(versions: 1);
        await EnableAsync(refId, workspaceId, 1, Plan(refId, TriggerA));

        await ProviderFactory.WorkflowDefinition(fixture.ConnectionString).InsertAsync(Row(refId, workspaceId), CancellationToken.None);

        WorkflowStateRow state = (await States.FindAsync(refId, workspaceId, CancellationToken.None))!;
        Assert.Equal((WorkflowState.Enabled, 1, 2), (state.State, state.LiveVersion!.Value, state.LatestVersion));
        Assert.Equal([ids[0]], await RegisteredDefinitionsAsync(refId));
    }

    [SkippableFact]
    public async Task Disabling_clears_everything_that_starts_a_run_and_returns_the_runs_still_going()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (refId, workspaceId, ids) = await PublishAsync(versions: 1);
        await EnableAsync(refId, workspaceId, 1, Plan(refId, TriggerA, withSchedule: true));
        RunRow running = await QueueBehindRunAsync(refId, ids[0], TriggerA);

        WorkflowDisableResult disabled = await States.DisableAsync(refId, workspaceId, User, deprecate: false, CancellationToken.None);

        Assert.True(disabled.Changed);
        Assert.Equal(ids[0], disabled.PreviousLiveDefinitionId);
        Assert.Equal(1, disabled.DroppedQueuedEvents);
        Assert.Equal([running.Id], disabled.ActiveRunIds);
        Assert.Equal((0, 0, 0), (await RegistrationsAsync(refId), await SchedulesAsync(refId), await QueuedAsync(refId, TriggerA)));
        Assert.Equal(WorkflowState.Disabled, (await States.FindAsync(refId, workspaceId, CancellationToken.None))!.State);
    }

    [SkippableFact]
    public async Task Disabling_a_disabled_workflow_changes_nothing_but_still_lists_its_runs()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (refId, workspaceId, ids) = await PublishAsync(versions: 1);
        await EnableAsync(refId, workspaceId, 1, Plan(refId, TriggerA));
        RunRow running = await QueueBehindRunAsync(refId, ids[0], TriggerA);
        await States.DisableAsync(refId, workspaceId, User, deprecate: false, CancellationToken.None);

        WorkflowDisableResult again = await States.DisableAsync(refId, workspaceId, User, deprecate: false, CancellationToken.None);

        Assert.False(again.Changed);
        Assert.Null(again.PreviousLiveDefinitionId);
        Assert.Equal([running.Id], again.ActiveRunIds);
    }

    [SkippableFact]
    public async Task A_disabled_workflow_can_be_enabled_again()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (refId, workspaceId, ids) = await PublishAsync(versions: 1);
        await EnableAsync(refId, workspaceId, 1, Plan(refId, TriggerA));
        await States.DisableAsync(refId, workspaceId, User, deprecate: false, CancellationToken.None);

        WorkflowEnableResult enabled = await EnableAsync(refId, workspaceId, 1, Plan(refId, TriggerA));

        Assert.Equal(new WorkflowEnableResult(true, null, ids[0], 0), enabled);
        Assert.Equal(1, await RegistrationsAsync(refId));
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Deprecating_from_enabled_or_disabled_takes_it_out_of_service(bool enabledFirst)
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (refId, workspaceId, _) = await PublishAsync(versions: 1);
        if (enabledFirst)
        {
            await EnableAsync(refId, workspaceId, 1, Plan(refId, TriggerA, withSchedule: true));
        }

        WorkflowDisableResult deprecated = await States.DisableAsync(refId, workspaceId, User, deprecate: true, CancellationToken.None);
        WorkflowDisableResult again = await States.DisableAsync(refId, workspaceId, User, deprecate: true, CancellationToken.None);

        Assert.True(deprecated.Changed);
        Assert.False(again.Changed);
        Assert.Equal(WorkflowState.Deprecated, (await States.FindAsync(refId, workspaceId, CancellationToken.None))!.State);
        Assert.Equal((0, 0), (await RegistrationsAsync(refId), await SchedulesAsync(refId)));
    }

    [SkippableFact]
    public async Task A_deprecated_workflow_cannot_be_enabled_or_disabled_until_it_is_reinstated_and_comes_back_disabled()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (refId, workspaceId, _) = await PublishAsync(versions: 1);
        await States.DisableAsync(refId, workspaceId, User, deprecate: true, CancellationToken.None);

        await AssertRefusedAsync(Deprecated, () => EnableAsync(refId, workspaceId, 1, Plan(refId, TriggerA)));
        await AssertRefusedAsync(Deprecated, () => States.DisableAsync(refId, workspaceId, User, deprecate: false, CancellationToken.None));

        await States.ReinstateAsync(refId, workspaceId, User, CancellationToken.None);

        Assert.Equal(WorkflowState.Disabled, (await States.FindAsync(refId, workspaceId, CancellationToken.None))!.State);
        Assert.Equal(0, await RegistrationsAsync(refId));
        await EnableAsync(refId, workspaceId, 1, Plan(refId, TriggerA));
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reinstating_a_workflow_that_is_not_deprecated_is_refused(bool enabled)
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (refId, workspaceId, _) = await PublishAsync(versions: 1);
        if (enabled)
        {
            await EnableAsync(refId, workspaceId, 1, Plan(refId, TriggerA));
        }

        await AssertRefusedAsync(NotDeprecated, () => States.ReinstateAsync(refId, workspaceId, User, CancellationToken.None));
    }

    [SkippableFact]
    public async Task A_version_that_does_not_exist_or_is_a_test_copy_cannot_be_enabled()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (refId, workspaceId, _) = await PublishAsync(versions: 1);
        await ProviderFactory.WorkflowDefinition(fixture.ConnectionString).InsertTestCopyAsync(Row(refId, workspaceId) with { Version = 1 }, CancellationToken.None);

        await AssertRefusedAsync(NoSuchVersion, () => EnableAsync(refId, workspaceId, 2, Plan(refId, TriggerA)));
        await AssertRefusedAsync(NoSuchVersion, () => EnableAsync(refId, workspaceId, 0, Plan(refId, TriggerA)));
        Assert.Equal(WorkflowState.Disabled, (await States.FindAsync(refId, workspaceId, CancellationToken.None))!.State);
    }

    [SkippableFact]
    public async Task Another_workspaces_unknown_or_deleted_workflow_is_not_found_for_every_action()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (refId, workspaceId, _) = await PublishAsync(versions: 1);
        var (deleted, deletedWorkspace, _) = await PublishAsync(versions: 1);
        await ProviderFactory.WorkflowDefinition(fixture.ConnectionString).DeleteAsync(deleted, deletedWorkspace, User, CancellationToken.None);

        foreach ((Guid target, int workspace) in new[] { (refId, workspaceId + 1), (Guid.NewGuid(), workspaceId), (deleted, deletedWorkspace) })
        {
            Assert.Null(await States.FindAsync(target, workspace, CancellationToken.None));
            await AssertRefusedAsync(NotFound, () => EnableAsync(target, workspace, 1, Plan(target, TriggerA)));
            await AssertRefusedAsync(NotFound, () => States.DisableAsync(target, workspace, User, deprecate: false, CancellationToken.None));
            await AssertRefusedAsync(NotFound, () => States.DisableAsync(target, workspace, User, deprecate: true, CancellationToken.None));
            await AssertRefusedAsync(NotFound, () => States.ReinstateAsync(target, workspace, User, CancellationToken.None));
        }

        Assert.Equal(WorkflowState.Disabled, (await States.FindAsync(refId, workspaceId, CancellationToken.None))!.State);
    }

    [SkippableFact]
    public async Task Enabling_a_version_whose_integration_is_gone_is_refused_and_leaves_the_live_version_alone()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (refId, workspaceId, ids) = await PublishAsync(versions: 2);
        await EnableAsync(refId, workspaceId, 1, Plan(refId, TriggerA));

        await AssertRefusedAsync(IntegrationMissing, () => States.EnableAsync(
            refId, workspaceId, 2, User, Plan(refId, TriggerB),
            [new Wbskt.Workflow.Abstraction.Integrations.WorkflowIntegrationRef(Guid.NewGuid(), "missing", Guid.NewGuid())],
            CancellationToken.None));

        Assert.Equal(1, (await States.FindAsync(refId, workspaceId, CancellationToken.None))!.LiveVersion);
        Assert.Equal([ids[0]], await RegisteredDefinitionsAsync(refId));
    }

    [SkippableFact]
    public async Task Run_Start_refuses_every_version_but_the_live_one()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (refId, workspaceId, ids) = await PublishAsync(versions: 2);
        WorkflowDefinitionRow v1 = await GetAsync(ids[0]);
        WorkflowDefinitionRow v2 = await GetAsync(ids[1]);

        await AssertRefusedAsync(NotLive, () => StartAsync(v1), "a disabled workflow starts nothing");

        await EnableAsync(refId, workspaceId, 2, Plan(refId, TriggerA));
        await AssertRefusedAsync(NotLive, () => StartAsync(v1), "only the live version starts");
        Assert.False((await StartAsync(v2)).Run.IsTest);

        await States.DisableAsync(refId, workspaceId, User, deprecate: true, CancellationToken.None);
        await AssertRefusedAsync(NotLive, () => StartAsync(v2), "a deprecated workflow starts nothing");
    }

    [SkippableFact]
    public async Task A_test_run_starts_while_the_workflow_is_disabled_but_not_once_it_is_deleted()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (refId, workspaceId, _) = await PublishAsync(versions: 1);
        var definitions = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        WorkflowDefinitionRow copy = await definitions.InsertTestCopyAsync(Row(refId, workspaceId) with { Version = 1 }, CancellationToken.None);

        Assert.True((await StartAsync(copy, isTest: true)).Run.IsTest);

        await definitions.DeleteAsync(refId, workspaceId, User, CancellationToken.None);
        await AssertRefusedAsync(DeletedError, () => StartAsync(copy, isTest: true));
    }

    [SkippableFact]
    public async Task A_child_started_by_a_parent_needs_its_own_workflow_live()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (childRef, workspaceId, ids) = await PublishAsync(versions: 1);
        WorkflowDefinitionRow child = await GetAsync(ids[0]);
        Guid parentRun = Guid.NewGuid();

        await AssertRefusedAsync(NotLive, () => StartAsync(child, parentRunRefId: parentRun));

        await EnableAsync(childRef, workspaceId, 1, Plan(childRef, TriggerA));
        Assert.Equal(parentRun, (await StartAsync(child, parentRunRefId: parentRun)).Run.ParentRunRefId);
    }

    [SkippableFact]
    public async Task Concurrent_switches_leave_exactly_one_live_version_with_its_own_registrations()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (refId, workspaceId, ids) = await PublishAsync(versions: 4);

        await Task.WhenAll(Enumerable.Range(1, 4).SelectMany(_ => new[] { 1, 2, 3, 4 })
            .Select(version => Task.Run(() => EnableAsync(refId, workspaceId, version, Plan(refId, TriggerA, withSchedule: true)))));

        WorkflowStateRow state = (await States.FindAsync(refId, workspaceId, CancellationToken.None))!;
        Assert.Equal([state.LiveDefinitionId!.Value], await RegisteredDefinitionsAsync(refId));
        Assert.Equal(1, await SchedulesAsync(refId));
        Assert.Contains(state.LiveDefinitionId.Value, ids);
    }

    [SkippableFact]
    public async Task A_disable_racing_an_enable_never_leaves_registrations_without_a_live_version()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var (refId, workspaceId, _) = await PublishAsync(versions: 2);
        await EnableAsync(refId, workspaceId, 1, Plan(refId, TriggerA));

        for (int i = 0; i < 10; i++)
        {
            await Task.WhenAll(
                Task.Run(() => EnableAsync(refId, workspaceId, 1 + (i % 2), Plan(refId, TriggerA))),
                Task.Run(() => States.DisableAsync(refId, workspaceId, User, deprecate: false, CancellationToken.None)));

            WorkflowStateRow state = (await States.FindAsync(refId, workspaceId, CancellationToken.None))!;
            int registrations = await RegistrationsAsync(refId);
            Assert.Equal(state.State == WorkflowState.Enabled ? 1 : 0, registrations);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────

    private async Task<(Guid RefId, int WorkspaceId, int[] DefinitionIds)> PublishAsync(int versions)
    {
        Guid refId = Guid.NewGuid();
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue - 1);
        var definitions = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        var ids = new int[versions];
        for (int i = 0; i < versions; i++)
        {
            ids[i] = (await definitions.InsertAsync(Row(refId, workspaceId), CancellationToken.None)).Id;
        }

        return (refId, workspaceId, ids);
    }

    private Task<WorkflowEnableResult> EnableAsync(Guid refId, int workspaceId, int version, TriggerPlan plan) =>
        States.EnableAsync(refId, workspaceId, version, User, plan, [], CancellationToken.None);

    private static TriggerPlan Plan(Guid refId, Guid triggerNodeId, bool withSchedule = false) =>
        new([Registration(refId, triggerNodeId)], withSchedule ? [new PlannedSchedule(triggerNodeId, "*/5 * * * *", DateTime.UtcNow.AddMinutes(5))] : []);

    private static PlannedTrigger Registration(Guid refId, Guid triggerNodeId) =>
        new(triggerNodeId, "manual", $"it-state:{refId}:{triggerNodeId}", null, "Queue", null, null, null);

    private Task<WorkflowDefinitionRow> GetAsync(int id) =>
        ProviderFactory.WorkflowDefinition(fixture.ConnectionString).GetByIdAsync(id, CancellationToken.None);

    private async Task<RunRow> QueueBehindRunAsync(Guid refId, int definitionId, Guid triggerNodeId)
    {
        string correlationKey = $"it-state-{Guid.NewGuid():N}";
        RunRow run = await ActiveRunSeeder.StartAsync(fixture.ConnectionString, definitionId, refId, triggerNodeId, correlationKey);
        await ProviderFactory.PendingTriggerEvent(fixture.ConnectionString).EnqueueBehindAsync(refId, triggerNodeId, correlationKey, "{}", null, CancellationToken.None);
        return run;
    }

    private Task<RunStartRow> StartAsync(WorkflowDefinitionRow definition, bool isTest = false, Guid? parentRunRefId = null)
    {
        DateTime now = DateTime.UtcNow;
        var run = new RunRow
        {
            Id = 0, RefId = Guid.NewGuid(),
            WorkflowDefinitionId = definition.Id,
            WorkflowRefId = definition.RefId,
            WorkflowVersion = definition.Version,
            TriggerNodeId = TriggerA,
            CorrelationKey = $"it-state:{Guid.NewGuid():N}",
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
            NodeId = TriggerA,
            Status = BranchStatus.Active,
            PendingTakePort = null,
            LocalJson = """{"trigger":{}}""",
            CreatedAt = now, UpdatedAt = now,
            RowVersion = Array.Empty<byte>()
        };
        return ProviderFactory.Run(fixture.ConnectionString).StartAsync(run, branch, null, CancellationToken.None);
    }

    private static async Task AssertRefusedAsync(int number, Func<Task> action, string? because = null)
    {
        SqlException refused = await Assert.ThrowsAsync<SqlException>(action);
        Assert.True(number == refused.Number, $"expected error {number} but got {refused.Number} ({refused.Message}) {because}");
    }

    private Task<int> RegistrationsAsync(Guid refId) => CountAsync("SELECT COUNT(*) FROM dbo.TriggerRegistrations WHERE WorkflowRefId = @p0", refId);

    private Task<int> SchedulesAsync(Guid refId) => CountAsync("SELECT COUNT(*) FROM dbo.ScheduledFires WHERE WorkflowRefId = @p0", refId);

    private Task<int> QueuedAsync(Guid refId, Guid triggerNodeId) =>
        CountAsync("SELECT COUNT(*) FROM dbo.PendingTriggerEvents WHERE WorkflowRefId = @p0 AND TriggerNodeId = @p1", refId, triggerNodeId);

    private async Task<int[]> RegisteredDefinitionsAsync(Guid refId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT DISTINCT WorkflowDefinitionId FROM dbo.TriggerRegistrations WHERE WorkflowRefId = @p0", connection);
        command.Parameters.AddWithValue("@p0", refId);
        var ids = new List<int>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetInt32(0));
        }

        return [.. ids];
    }

    private async Task<int> CountAsync(string sql, params object[] args)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        for (int i = 0; i < args.Length; i++)
        {
            command.Parameters.AddWithValue($"@p{i}", args[i]);
        }

        return (int)(await command.ExecuteScalarAsync())!;
    }

    private static WorkflowDefinitionRow Row(Guid refId, int workspaceId) => new()
    {
        Id = 0,
        RefId = refId,
        Version = 0,
        WorkspaceId = workspaceId,
        Name = "it-state",
        Description = null,
        DefinitionJson = """{"nodes":[],"edges":[]}""",
        PublishedBy = 1,
        CreatedAt = DateTime.UtcNow
    };
}

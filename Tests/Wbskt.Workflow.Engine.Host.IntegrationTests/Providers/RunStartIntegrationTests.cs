using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// Run_Start creates a triggered run whole (run, counters, root branch) and at most once per trigger
/// start key, so a retried inbound event recovers the run an earlier attempt created (#293).
/// </summary>
[Collection("SqlEdge")]
public sealed class RunStartIntegrationTests(SqlEdgeFixture fixture)
{
    private const int WorkspaceRunCeilingError = 50026;
    private const int ActiveRunConflictError = 50028;
    private const string SkipReason = "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.";

    [SkippableFact]
    public async Task Start_creates_the_run_its_counters_and_its_root_branch_together()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        WorkflowDefinitionRow wd = await CreateDefinitionAsync();
        (RunRow run, BranchRow branch) = NewRun(wd);

        RunStartRow started = await ProviderFactory.Run(fixture.ConnectionString).StartAsync(run, branch, $"trigger-start:{Guid.NewGuid():N}", CancellationToken.None);

        started.Created.Should().BeTrue();
        started.Run.RefId.Should().Be(run.RefId);
        BranchRow root = await ProviderFactory.Branch(fixture.ConnectionString).GetByIdAsync(started.RootBranchId, CancellationToken.None);
        root.RefId.Should().Be(branch.RefId);
        root.RunId.Should().Be(started.Run.Id);
        root.Status.Should().Be(BranchStatus.Active);
        root.LocalJson.Should().Be(branch.LocalJson);
        RunCountersRow counters = await ProviderFactory.RunCounters(fixture.ConnectionString).GetByRunIdAsync(started.Run.Id, CancellationToken.None);
        counters.ActiveBranchCount.Should().Be(1);
    }

    [SkippableFact]
    public async Task Start_returns_the_existing_run_for_a_key_that_already_started_one()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        WorkflowDefinitionRow wd = await CreateDefinitionAsync();
        var runProvider = ProviderFactory.Run(fixture.ConnectionString);
        string key = $"trigger-start:{Guid.NewGuid():N}";
        (RunRow firstRun, BranchRow firstBranch) = NewRun(wd);
        (RunRow retryRun, BranchRow retryBranch) = NewRun(wd);

        RunStartRow first = await runProvider.StartAsync(firstRun, firstBranch, key, CancellationToken.None);
        RunStartRow retry = await runProvider.StartAsync(retryRun, retryBranch, key, CancellationToken.None);
        RunStartRow? found = await runProvider.FindByTriggerStartKeyAsync(key, CancellationToken.None);

        retry.Created.Should().BeFalse();
        retry.Run.Id.Should().Be(first.Run.Id);
        retry.RootBranchId.Should().Be(first.RootBranchId);
        found!.Run.Id.Should().Be(first.Run.Id);
        found.RootBranchId.Should().Be(first.RootBranchId);
        found.Created.Should().BeFalse();
        (await runProvider.FindRowByRefIdAsync(retryRun.RefId, CancellationToken.None)).Should().BeNull();
        (await ProviderFactory.Branch(fixture.ConnectionString).GetAllByRunIdAsync(first.Run.Id, CancellationToken.None)).Should().ContainSingle();
    }

    [SkippableFact]
    public async Task Concurrent_starts_under_one_key_create_one_run()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        WorkflowDefinitionRow wd = await CreateDefinitionAsync();
        string key = $"trigger-start:{Guid.NewGuid():N}";

        RunStartRow[] results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
        {
            (RunRow run, BranchRow branch) = NewRun(wd);
            return ProviderFactory.Run(fixture.ConnectionString).StartAsync(run, branch, key, CancellationToken.None);
        }));

        results.Count(r => r.Created).Should().Be(1);
        results.Select(r => r.Run.Id).Distinct().Should().ContainSingle();
        results.Select(r => r.RootBranchId).Distinct().Should().ContainSingle();
    }

    [SkippableFact]
    public async Task Start_without_a_key_always_creates_a_run()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        WorkflowDefinitionRow wd = await CreateDefinitionAsync();
        var runProvider = ProviderFactory.Run(fixture.ConnectionString);
        (RunRow a, BranchRow aBranch) = NewRun(wd);
        (RunRow b, BranchRow bBranch) = NewRun(wd);

        RunStartRow first = await runProvider.StartAsync(a, aBranch, null, CancellationToken.None);
        RunStartRow second = await runProvider.StartAsync(b, bBranch, null, CancellationToken.None);

        first.Created.Should().BeTrue();
        second.Created.Should().BeTrue();
        second.Run.Id.Should().NotBe(first.Run.Id);
        (await runProvider.FindByTriggerStartKeyAsync($"trigger-start:{Guid.NewGuid():N}", CancellationToken.None)).Should().BeNull();
    }

    [SkippableFact]
    public async Task Start_past_the_workspace_ceiling_is_refused()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int workspaceId = NewWorkspaceId();
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);
        var runProvider = ProviderFactory.Run(fixture.ConnectionString);
        for (int i = 0; i < 2; i++)
        {
            (RunRow run, BranchRow branch) = NewRun(wd);
            await runProvider.StartAsync(run, branch, null, workspaceId, 2, CancellationToken.None);
        }

        (RunRow extra, BranchRow extraBranch) = NewRun(wd);
        Func<Task> act = () => runProvider.StartAsync(extra, extraBranch, null, workspaceId, 2, CancellationToken.None);

        (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(WorkspaceRunCeilingError);
        (await runProvider.FindRowByRefIdAsync(extra.RefId, CancellationToken.None)).Should().BeNull();
    }

    [SkippableFact]
    public async Task Start_at_the_ceiling_still_returns_the_run_a_key_already_started()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int workspaceId = NewWorkspaceId();
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);
        var runProvider = ProviderFactory.Run(fixture.ConnectionString);
        string key = $"trigger-start:{Guid.NewGuid():N}";
        (RunRow first, BranchRow firstBranch) = NewRun(wd);
        (RunRow retry, BranchRow retryBranch) = NewRun(wd);

        RunStartRow started = await runProvider.StartAsync(first, firstBranch, key, workspaceId, 1, CancellationToken.None);
        RunStartRow again = await runProvider.StartAsync(retry, retryBranch, key, workspaceId, 1, CancellationToken.None);

        again.Created.Should().BeFalse();
        again.Run.Id.Should().Be(started.Run.Id);
    }

    [SkippableFact]
    public async Task A_finished_run_frees_its_place_under_the_ceiling()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int workspaceId = NewWorkspaceId();
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);
        var runProvider = ProviderFactory.Run(fixture.ConnectionString);
        (RunRow first, BranchRow firstBranch) = NewRun(wd);
        await runProvider.StartAsync(first, firstBranch, null, workspaceId, 1, CancellationToken.None);
        await runProvider.UpdateStatusAsync(first.RefId, RunStatus.Succeeded, DateTime.UtcNow, null, null, CancellationToken.None);

        (RunRow next, BranchRow nextBranch) = NewRun(wd);
        RunStartRow started = await runProvider.StartAsync(next, nextBranch, null, workspaceId, 1, CancellationToken.None);

        started.Created.Should().BeTrue();
    }

    [SkippableFact]
    public async Task Concurrent_starts_never_pass_the_ceiling()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int workspaceId = NewWorkspaceId();
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);

        Task<RunStartRow?>[] attempts = Enumerable.Range(0, 12).Select(async _ =>
        {
            (RunRow run, BranchRow branch) = NewRun(wd);
            try
            {
                return await ProviderFactory.Run(fixture.ConnectionString).StartAsync(run, branch, null, workspaceId, 3, CancellationToken.None);
            }
            catch (SqlException ex) when (ex.Number == WorkspaceRunCeilingError)
            {
                return null;
            }
        }).ToArray();
        RunStartRow?[] results = await Task.WhenAll(attempts);

        results.Count(r => r is not null).Should().Be(3);
    }

    [SkippableFact]
    public async Task The_ceiling_counts_only_its_own_workspace()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int busy = NewWorkspaceId();
        int other = NewWorkspaceId();
        WorkflowDefinitionRow busyWd = await CreateDefinitionAsync(busy);
        WorkflowDefinitionRow otherWd = await CreateDefinitionAsync(other);
        var runProvider = ProviderFactory.Run(fixture.ConnectionString);
        (RunRow a, BranchRow aBranch) = NewRun(busyWd);
        await runProvider.StartAsync(a, aBranch, null, busy, 1, CancellationToken.None);

        (RunRow b, BranchRow bBranch) = NewRun(otherWd);
        RunStartRow started = await runProvider.StartAsync(b, bBranch, null, other, 1, CancellationToken.None);

        started.Created.Should().BeTrue();
    }

    [SkippableFact]
    public async Task A_start_sent_without_a_ceiling_starts_in_a_full_workspace()
    {
        // What RunStarter sends for an exempt start: a sub-workflow child, a queued event's replay or a
        // run replacing the ones it cancels.
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int workspaceId = NewWorkspaceId();
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);
        var runProvider = ProviderFactory.Run(fixture.ConnectionString);
        (RunRow first, BranchRow firstBranch) = NewRun(wd);
        await runProvider.StartAsync(first, firstBranch, null, workspaceId, 1, CancellationToken.None);

        (RunRow exempt, BranchRow exemptBranch) = NewRun(wd);
        RunStartRow started = await runProvider.StartAsync(exempt, exemptBranch, null, workspaceId, null, CancellationToken.None);

        started.Created.Should().BeTrue();
    }

    [SkippableFact]
    public async Task A_start_that_cannot_get_the_workspace_lock_in_time_is_refused()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int workspaceId = NewWorkspaceId();
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);

        // Another session holds the workspace's ceiling lock for longer than Run_Start will wait.
        await using var holder = new SqlConnection(fixture.ConnectionString);
        await holder.OpenAsync();
        await using SqlTransaction tx = (SqlTransaction)await holder.BeginTransactionAsync();
        await using (var hold = new SqlCommand("EXEC sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction';", holder, tx))
        {
            hold.Parameters.AddWithValue("@Resource", $"run-ceiling:{workspaceId}");
            await hold.ExecuteNonQueryAsync();
        }

        (RunRow run, BranchRow branch) = NewRun(wd);
        Func<Task> act = () => ProviderFactory.Run(fixture.ConnectionString).StartAsync(run, branch, null, workspaceId, 10, CancellationToken.None);

        (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(WorkspaceRunCeilingError);
        await tx.RollbackAsync();
        (await ProviderFactory.Run(fixture.ConnectionString).FindRowByRefIdAsync(run.RefId, CancellationToken.None)).Should().BeNull();
    }

    [SkippableFact]
    public async Task A_guarded_start_is_refused_while_a_run_holds_its_key()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        WorkflowDefinitionRow wd = await CreateDefinitionAsync();
        var runProvider = ProviderFactory.Run(fixture.ConnectionString);
        (RunRow first, BranchRow firstBranch) = NewRun(wd);
        await runProvider.StartAsync(first, firstBranch, null, 0, null, ActiveRunGuard.AnyActive, CancellationToken.None);

        (RunRow sameKey, BranchRow sameKeyBranch) = SameTrigger(first, wd, first.CorrelationKey!);
        Func<Task> act = () => runProvider.StartAsync(sameKey, sameKeyBranch, null, 0, null, ActiveRunGuard.AnyActive, CancellationToken.None);
        (RunRow otherKey, BranchRow otherKeyBranch) = SameTrigger(first, wd, "another-key");
        RunStartRow otherStarted = await runProvider.StartAsync(otherKey, otherKeyBranch, null, 0, null, ActiveRunGuard.AnyActive, CancellationToken.None);

        (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(ActiveRunConflictError);
        (await runProvider.FindRowByRefIdAsync(sameKey.RefId, CancellationToken.None)).Should().BeNull();
        otherStarted.Created.Should().BeTrue();
    }

    [SkippableFact]
    public async Task A_start_guarded_for_CancelExisting_passes_a_run_that_is_cancelling()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        WorkflowDefinitionRow wd = await CreateDefinitionAsync();
        var runProvider = ProviderFactory.Run(fixture.ConnectionString);
        (RunRow first, BranchRow firstBranch) = NewRun(wd);
        RunStartRow firstStarted = await runProvider.StartAsync(first, firstBranch, null, 0, null, ActiveRunGuard.None, CancellationToken.None);
        await runProvider.TransitionStatusAsync(firstStarted.Run.Id, RunStatus.Running, RunStatus.Cancelling, DateTime.UtcNow, "IT", CancellationToken.None);

        (RunRow drop, BranchRow dropBranch) = SameTrigger(first, wd, first.CorrelationKey!);
        Func<Task> dropAct = () => runProvider.StartAsync(drop, dropBranch, null, 0, null, ActiveRunGuard.AnyActive, CancellationToken.None);
        (RunRow replace, BranchRow replaceBranch) = SameTrigger(first, wd, first.CorrelationKey!);
        RunStartRow replaced = await runProvider.StartAsync(replace, replaceBranch, null, 0, null, ActiveRunGuard.NotCancelling, CancellationToken.None);

        replaced.Created.Should().BeTrue();
        (await dropAct.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(ActiveRunConflictError);
    }

    [SkippableFact]
    public async Task Guarded_starts_that_meet_on_one_key_create_one_run()
    {
        // #467. Both starts are held at the key's lock until both are waiting, so they really do meet. The
        // keys differ only in case, which the database's collation treats as one key, so they share a lock.
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        WorkflowDefinitionRow wd = await CreateDefinitionAsync();
        var runProvider = ProviderFactory.Run(fixture.ConnectionString);
        (RunRow a, BranchRow aBranch) = NewRun(wd);
        (RunRow b, BranchRow bBranch) = SameTrigger(a, wd, a.CorrelationKey!.ToUpperInvariant());

        await using var holder = new SqlConnection(fixture.ConnectionString);
        await holder.OpenAsync();
        await using SqlTransaction tx = (SqlTransaction)await holder.BeginTransactionAsync();
        await using (var hold = new SqlCommand(
            """
            DECLARE @Resource NVARCHAR(255) = CONCAT(N'trigger-start:', @WorkflowRefId, N':', @TriggerNodeId, N':',
                CONVERT(NVARCHAR(64), HASHBYTES('SHA2_256', UPPER(@CorrelationKey)), 2));
            EXEC sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction';
            """, holder, tx))
        {
            hold.Parameters.AddWithValue("@WorkflowRefId", a.WorkflowRefId);
            hold.Parameters.AddWithValue("@TriggerNodeId", a.TriggerNodeId);
            hold.Parameters.AddWithValue("@CorrelationKey", a.CorrelationKey!);
            await hold.ExecuteNonQueryAsync();
        }

        Task<RunStartRow> startA = runProvider.StartAsync(a, aBranch, null, 0, null, ActiveRunGuard.AnyActive, CancellationToken.None);
        Task<RunStartRow> startB = runProvider.StartAsync(b, bBranch, null, 0, null, ActiveRunGuard.AnyActive, CancellationToken.None);
        await WaitForAppLockWaitersAsync(2);
        await tx.RollbackAsync();

        Task all = Task.WhenAll(startA, startB);
        try { await all; } catch (SqlException) { }

        new[] { startA, startB }.Count(t => t.IsCompletedSuccessfully).Should().Be(1);
        Task<RunStartRow> refused = startA.IsCompletedSuccessfully ? startB : startA;
        refused.Exception!.InnerException.Should().BeOfType<SqlException>().Which.Number.Should().Be(ActiveRunConflictError);
    }

    private static (RunRow Run, BranchRow Branch) SameTrigger(RunRow existing, WorkflowDefinitionRow wd, string correlationKey)
    {
        (RunRow run, BranchRow branch) = NewRun(wd);
        return (run with { TriggerNodeId = existing.TriggerNodeId, CorrelationKey = correlationKey }, branch with { NodeId = existing.TriggerNodeId });
    }

    private async Task WaitForAppLockWaitersAsync(int count)
    {
        await using var probe = new SqlConnection(fixture.ConnectionString);
        await probe.OpenAsync();
        for (int i = 0; i < 100; i++)
        {
            await using var cmd = new SqlCommand("SELECT COUNT(*) FROM sys.dm_tran_locks WHERE resource_type = 'APPLICATION' AND request_status = 'WAIT'", probe);
            if ((int)(await cmd.ExecuteScalarAsync())! >= count)
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Fewer than {count} sessions ever waited on an application lock.");
    }

    private static int NewWorkspaceId() => Random.Shared.Next(1_000_000, int.MaxValue);

    private async Task<WorkflowDefinitionRow> CreateDefinitionAsync(int workspaceId = 1)
    {
        return await ProviderFactory.WorkflowDefinition(fixture.ConnectionString).InsertAsync(new WorkflowDefinitionRow
        {
            Id = 0, RefId = Guid.NewGuid(), Version = 0, WorkspaceId = workspaceId,
            Name = $"IT-WD-{Guid.NewGuid():N}",
            Description = null, IsEnabled = true,
            DefinitionJson = """{"nodes":[],"edges":[]}""",
            PublishedBy = 1, CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);
    }

    private static (RunRow Run, BranchRow Branch) NewRun(WorkflowDefinitionRow wd)
    {
        DateTime now = DateTime.UtcNow;
        Guid triggerNodeId = Guid.NewGuid();
        var run = new RunRow
        {
            Id = 0, RefId = Guid.NewGuid(),
            WorkflowDefinitionId = wd.Id,
            WorkflowRefId = wd.RefId,
            WorkflowVersion = wd.Version,
            TriggerNodeId = triggerNodeId,
            CorrelationKey = "it",
            Status = RunStatus.Running,
            StartedAt = now,
            CompletedAt = null,
            CancellationRequestedAt = null,
            CancellationReason = null,
            CreditBudget = 10m,
            CreatedAt = now
        };
        var branch = new BranchRow
        {
            Id = 0, RefId = Guid.NewGuid(), RunId = 0,
            ParentBranchId = null, ForkCohortId = null,
            NodeId = triggerNodeId,
            Status = BranchStatus.Active,
            PendingTakePort = null,
            LocalJson = """{"trigger":{"n":1}}""",
            LastOutputJson = null,
            CreatedAt = now, UpdatedAt = now,
            RowVersion = Array.Empty<byte>()
        };
        return (run, branch);
    }
}

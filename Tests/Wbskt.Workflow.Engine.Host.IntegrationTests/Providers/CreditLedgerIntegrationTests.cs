using Microsoft.Data.SqlClient;
using Wbskt.Models.Billing;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// The credit ledger (dbo.WorkspaceCreditUsage): RunCredits_Post moves what runs consumed into it
/// exactly once, and Run_Start refuses a start once the tenant has spent its plan's credits for the
/// month (#627).
/// </summary>
[Collection("SqlEdge")]
public sealed class CreditLedgerIntegrationTests(SqlEdgeFixture fixture)
{
    private const int PlanCreditLimitError = 50033;
    private const int WorkspaceCreditCapError = 50034;
    private const string SkipReason = "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.";

    [SkippableFact]
    public async Task The_ledger_adds_up_to_what_the_runs_consumed_and_a_repeat_post_adds_nothing()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int workspaceId = NewId();
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);
        long first = await StartAsync(wd, startCost: 1m);
        long second = await StartAsync(wd, startCost: 1m);
        await ChargeAsync(first, 2.5m);
        await ChargeAsync(second, 0.25m);
        await PostAsync();
        await ProviderFactory.Run(fixture.ConnectionString).SetTerminalAsync(first, RunStatus.Succeeded.ToString(), DateTime.UtcNow, CancellationToken.None);

        // A charge that lands after the run ended, and one on a run still going.
        await ChargeAsync(first, 1m);
        await ChargeAsync(second, 3m);
        await PostAsync();
        decimal posted = await LedgerAsync(workspaceId);
        await PostAsync();

        posted.Should().Be(1m + 2.5m + 1m + 1m + 0.25m + 3m);
        posted.Should().Be(await ConsumedAsync(wd.Id));
        (await LedgerAsync(workspaceId)).Should().Be(posted);
    }

    [SkippableFact]
    public async Task Deleting_a_retired_run_posts_what_it_had_not_posted_first()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int workspaceId = NewId();
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);
        long runId = await StartAsync(wd, startCost: 1m);
        await ChargeAsync(runId, 4m);
        await ProviderFactory.Run(fixture.ConnectionString).SetTerminalAsync(runId, RunStatus.Succeeded.ToString(), new DateTime(2000, 1, 1), CancellationToken.None);

        await ProviderFactory.RunRetention(fixture.ConnectionString).DeleteRetiredRunsAsync(new DateTime(2001, 1, 1), [new RunHistoryRetention(PlanCode.Free, 36_500)], new DateTime(2001, 1, 1), 100, CancellationToken.None);

        (await LedgerAsync(workspaceId)).Should().Be(5m);
    }

    [SkippableFact]
    public async Task A_tenant_that_spent_its_credits_cannot_start_a_run_in_any_of_its_workspaces()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int tenantId = NewId();
        int spender = NewId();
        int sibling = NewId();
        int stranger = NewId();
        await AddAccountAsync(spender, tenantId);
        await AddAccountAsync(sibling, tenantId);
        await AddAccountAsync(stranger, tenantId + 1);
        WorkflowDefinitionRow spenderWd = await CreateDefinitionAsync(spender);
        WorkflowDefinitionRow siblingWd = await CreateDefinitionAsync(sibling);
        WorkflowDefinitionRow strangerWd = await CreateDefinitionAsync(stranger);
        long spent = await StartAsync(spenderWd, startCost: 1m);
        await ChargeAsync(spent, 4m);
        await PostAsync();

        Func<Task> refused = () => StartAsync(siblingWd, credits: new PlanCredits(5m, tenantId));
        long other = await StartAsync(strangerWd, credits: new PlanCredits(5m, tenantId + 1));

        (await refused.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(PlanCreditLimitError);
        other.Should().BePositive();
    }

    /// <summary>
    /// Deleting the workspace that spent the month's credits used to delete its account copy, so its
    /// ledger rows dropped out of the tenant's sum and a new workspace started with the allowance again.
    /// </summary>
    [SkippableFact]
    public async Task Credits_a_deleted_workspace_spent_still_count_against_the_tenant()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int tenantId = NewId();
        int deleted = NewId();
        int replacement = NewId();
        await AddAccountAsync(deleted, tenantId);
        await AddAccountAsync(replacement, tenantId);
        WorkflowDefinitionRow deletedWd = await CreateDefinitionAsync(deleted);
        WorkflowDefinitionRow replacementWd = await CreateDefinitionAsync(replacement);
        long spent = await StartAsync(deletedWd, startCost: 1m);
        await ChargeAsync(spent, 4m);
        await PostAsync();

        await RetireAsync(deleted);
        Func<Task> refused = () => StartAsync(replacementWd, credits: new PlanCredits(5m, tenantId));

        (await refused.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(PlanCreditLimitError);
        (await RetiredAtAsync(deleted)).Should().NotBeNull("the copy is kept and marked, not deleted");
    }

    [SkippableFact]
    public async Task Credits_a_running_run_has_not_posted_yet_still_count()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int workspaceId = NewId();
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);
        long running = await StartAsync(wd, startCost: 1m);
        await ChargeAsync(running, 4m);

        Func<Task> refused = () => StartAsync(wd, credits: new PlanCredits(5m, null));

        (await refused.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(PlanCreditLimitError);
    }

    [SkippableFact]
    public async Task Credits_an_ended_run_has_not_posted_yet_still_count_against_the_plan_and_the_cap()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int workspaceId = NewId();
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);
        long ended = await StartAsync(wd, startCost: 1m);
        await ChargeAsync(ended, 4m);
        await ProviderFactory.Run(fixture.ConnectionString).SetTerminalAsync(ended, RunStatus.Succeeded.ToString(), DateTime.UtcNow, CancellationToken.None);

        Func<Task> overPlan = () => StartAsync(wd, credits: new PlanCredits(5m, null));
        (await overPlan.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(PlanCreditLimitError);

        decimal spent = await SetCapAsync(workspaceId, 5m);
        Func<Task> overCap = () => StartAsync(wd, credits: new PlanCredits(1000m, null));
        (await overCap.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(WorkspaceCreditCapError);

        spent.Should().Be(5m);
    }

    [SkippableFact]
    public async Task A_post_clears_an_ended_runs_unposted_mark_and_counts_its_credits_once()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int workspaceId = NewId();
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);
        long ended = await StartAsync(wd, startCost: 1m);
        await ChargeAsync(ended, 2m);
        await ProviderFactory.Run(fixture.ConnectionString).SetTerminalAsync(ended, RunStatus.Succeeded.ToString(), DateTime.UtcNow, CancellationToken.None);

        await PostAsync();

        (await ScalarAsync("SELECT CAST(COUNT(*) AS DECIMAL(18, 4)) FROM dbo.RunCounters WHERE RunId = @Id AND HasUnposted = 1", (int)ended)).Should().Be(0m);
        (await LedgerAsync(workspaceId)).Should().Be(3m);
        (await SetCapAsync(workspaceId, null)).Should().Be(3m, "the posted credits are not counted again as unposted");
    }

    [SkippableFact]
    public async Task A_run_started_near_the_end_of_the_credits_gets_only_what_is_left_as_its_budget()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int workspaceId = NewId();
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);
        long first = await StartAsync(wd, startCost: 1m);
        await ChargeAsync(first, 1m);
        await PostAsync();

        long second = await StartAsync(wd, credits: new PlanCredits(5m, null));

        RunRow run = await ProviderFactory.Run(fixture.ConnectionString).GetByIdAsync(second, CancellationToken.None);
        run.CreditBudget.Should().Be(3m);
    }

    [SkippableFact]
    public async Task A_workspace_at_its_cap_cannot_start_a_run_while_an_uncapped_one_of_the_same_tenant_can()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int tenantId = NewId();
        int capped = NewId();
        int uncapped = NewId();
        await AddAccountAsync(capped, tenantId);
        await AddAccountAsync(uncapped, tenantId);
        WorkflowDefinitionRow cappedWd = await CreateDefinitionAsync(capped);
        WorkflowDefinitionRow uncappedWd = await CreateDefinitionAsync(uncapped);
        long spent = await StartAsync(cappedWd, startCost: 1m);
        await ChargeAsync(spent, 2m);
        await PostAsync();
        decimal reported = await SetCapAsync(capped, 3m);

        Func<Task> refused = () => StartAsync(cappedWd, credits: new PlanCredits(1000m, tenantId));
        long other = await StartAsync(uncappedWd, credits: new PlanCredits(1000m, tenantId));

        reported.Should().Be(3m);
        (await refused.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(WorkspaceCreditCapError);
        other.Should().BePositive();
    }

    [SkippableFact]
    public async Task A_cap_cuts_the_budget_to_what_it_leaves_and_removing_it_lifts_that()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int workspaceId = NewId();
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);
        long first = await StartAsync(wd, startCost: 1m, budget: 1m);
        await SetCapAsync(workspaceId, 4m);
        long capped = await StartAsync(wd, credits: new PlanCredits(1000m, null));
        await SetCapAsync(workspaceId, null);
        long uncapped = await StartAsync(wd, credits: new PlanCredits(1000m, null));

        var runs = ProviderFactory.Run(fixture.ConnectionString);
        var counters = ProviderFactory.RunCounters(fixture.ConnectionString);
        (await runs.GetByIdAsync(capped, CancellationToken.None)).CreditBudget.Should().Be(0.2m, "a capped run leases a twentieth of the cap at a time");
        (await counters.TryChargeAsync((int)capped, 3m, CancellationToken.None)).Should().Be(new CreditCharge(3m, 3m), "the lease grows up to what the cap left");
        (await counters.TryChargeAsync((int)capped, 0.01m, CancellationToken.None)).Should().BeNull();
        (await runs.GetByIdAsync(uncapped, CancellationToken.None)).CreditBudget.Should().Be(100m);
        first.Should().BePositive();
    }

    [SkippableFact]
    public async Task A_post_reports_each_workspaces_month_and_its_cap()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int workspaceId = NewId();
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);
        await SetCapAsync(workspaceId, 10m);
        long runId = await StartAsync(wd, startCost: 1m);
        await ChargeAsync(runId, 7m);

        IReadOnlyCollection<WorkspaceCreditPost> posted = await PostAsync();

        posted.Should().ContainSingle(p => p.WorkspaceId == workspaceId)
            .Which.Should().Be(new WorkspaceCreditPost(workspaceId, 1, 8m, 8m, 10m));
    }

    [SkippableFact]
    public async Task Usage_counts_the_accounts_workspaces_and_splits_out_this_ones_share()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int tenantId = NewId();
        int mine = NewId();
        int sibling = NewId();
        await AddAccountAsync(mine, tenantId);
        await AddAccountAsync(sibling, tenantId);
        WorkflowDefinitionRow mineWd = await CreateDefinitionAsync(mine);
        WorkflowDefinitionRow siblingWd = await CreateDefinitionAsync(sibling);
        long posted = await StartAsync(mineWd, startCost: 1m);
        await ChargeAsync(posted, 2m);
        await PostAsync();
        await StartAsync(siblingWd, startCost: 4m);
        await SetCapAsync(mine, 50m);
        var usage = new Wbskt.Management.Host.Providers.WorkspaceUsageProvider(ProviderFactory.BuildConfiguration(fixture.ConnectionString));
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        var summary = await usage.GetSummaryAsync(mine, tenantId, new DateOnly(today.Year, today.Month, 1), CancellationToken.None);
        var days = await usage.GetDailyCreditsAsync(mine, today.AddDays(-1), today, CancellationToken.None);

        // The workspace's run still going holds what is left of its 100 budget after spending 3.
        summary.Should().Be(new Wbskt.Management.Host.Models.WorkspaceUsageSummary(7m, 3m, 0, 0, 0, 0, 2, 1, 50m, 0m, 97m, null, 0m));
        days.Should().Equal(new Wbskt.Management.Models.DailyCreditUsage(today, 3m));
    }

    private async Task<decimal> SetCapAsync(int workspaceId, decimal? cap)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("dbo.WorkspaceCreditCap_Set", connection) { CommandType = System.Data.CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@WorkspaceId", workspaceId);
        command.Parameters.Add("@MonthlyCreditCap", System.Data.SqlDbType.Decimal).Value = (object?)cap ?? DBNull.Value;
        command.Parameters["@MonthlyCreditCap"].Precision = 18;
        command.Parameters["@MonthlyCreditCap"].Scale = 4;
        command.Parameters.AddWithValue("@MonthStart", new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1));
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return reader.GetDecimal(reader.GetOrdinal("CreditsSpent"));
    }

    private static int NewId() => Random.Shared.Next(1_000_000, int.MaxValue - 1);

    private async Task<long> StartAsync(WorkflowDefinitionRow wd, decimal startCost = 0m, PlanCredits? credits = null, decimal budget = 100m)
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
            CreditBudget = budget,
            CreatedAt = now,
            StartCost = startCost
        };
        RunStartRow started = await ProviderFactory.Run(fixture.ConnectionString).StartAsync(
            run, BranchRow.New(Guid.NewGuid(), 0, triggerNodeId, """{"trigger":{}}""", now), null, wd.WorkspaceId, null, ActiveRunGuard.None, credits, CancellationToken.None);
        return started.Run.Id;
    }

    private async Task ChargeAsync(long runId, decimal cost)
    {
        CreditCharge? charge = await ProviderFactory.RunCounters(fixture.ConnectionString).TryChargeAsync((int)runId, cost, CancellationToken.None);
        charge.Should().NotBeNull();
    }

    private Task<IReadOnlyCollection<WorkspaceCreditPost>> PostAsync() =>
        ProviderFactory.CreditUsage(fixture.ConnectionString).PostAsync(DateOnly.FromDateTime(DateTime.UtcNow), DateTime.UtcNow.AddHours(-1), 500, CancellationToken.None);

    private Task<decimal> LedgerAsync(int workspaceId) =>
        ScalarAsync("SELECT ISNULL(SUM(Credits), 0) FROM dbo.WorkspaceCreditUsage WHERE WorkspaceId = @Id", workspaceId);

    private Task<decimal> ConsumedAsync(int workflowDefinitionId) =>
        ScalarAsync("SELECT ISNULL(SUM(rc.CreditsConsumed), 0) FROM dbo.RunCounters rc JOIN dbo.Runs r ON r.Id = rc.RunId WHERE r.WorkflowDefinitionId = @Id", workflowDefinitionId);

    private async Task<decimal> ScalarAsync(string sql, int id)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        return (decimal)(await command.ExecuteScalarAsync())!;
    }

    private async Task<DateTime?> RetiredAtAsync(int workspaceId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT RetiredAt FROM dbo.WorkspaceAccounts WHERE WorkspaceId = @Id", connection);
        command.Parameters.AddWithValue("@Id", workspaceId);
        return await command.ExecuteScalarAsync() as DateTime?;
    }

    private async Task RetireAsync(int workspaceId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("dbo.Workspace_Retire", connection) { CommandType = System.Data.CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@WorkspaceId", workspaceId);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
    }

    private async Task AddAccountAsync(int workspaceId, int tenantId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "INSERT INTO dbo.WorkspaceAccounts (WorkspaceId, TenantId, TenantRefId, PlanCode, PlanRevision) VALUES (@WorkspaceId, @TenantId, NEWID(), N'Free', 0)",
            connection);
        command.Parameters.AddWithValue("@WorkspaceId", workspaceId);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<WorkflowDefinitionRow> CreateDefinitionAsync(int workspaceId)
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
}

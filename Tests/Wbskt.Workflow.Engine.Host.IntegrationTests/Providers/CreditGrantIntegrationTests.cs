using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Management.Host.Providers;
using Wbskt.Models.Billing;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// Credit grants (top-ups, #627): Run_Start counts what a tenant's grants still hold on top of its plan's
/// monthly credits, a grant is recorded and revoked once per source, and credits posted past the plan's
/// allowance of the moment are drawn from the grants, soonest to expire first, as they are posted (#680).
/// Also the run budgets around them: a sub-workflow child spends from its parent's budget (#679), and a
/// workspace's cap reserves its running runs' budgets so it is never passed (#688).
/// </summary>
[Collection("SqlEdge")]
public sealed class CreditGrantIntegrationTests(SqlEdgeFixture fixture)
{
    private const int PlanCreditLimitError = 50033;
    private const int WorkspaceCreditCapError = 50034;
    private const string SkipReason = "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.";

    [SkippableFact]
    public async Task A_grant_lets_a_tenant_past_its_plans_credits_until_it_is_spent_or_revoked()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int tenantId = NewId();
        int workspaceId = NewId();
        await AddAccountAsync(workspaceId, tenantId);
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);
        long spent = await StartAsync(wd, startCost: 1m);
        await ChargeAsync(spent, 4m);
        var credits = new PlanCredits(5m, tenantId);
        Func<Task> start = () => StartAsync(wd, credits: credits);
        await start.Should().ThrowAsync<SqlException>();

        string payment = $"pay_{Guid.NewGuid():N}";
        ICreditGrantProvider grants = Grants();
        (await grants.InsertAsync(tenantId, 3m, DateTime.UtcNow, DateTime.UtcNow.AddMonths(12), "test", payment, CancellationToken.None)).Should().BeTrue();
        (await grants.InsertAsync(tenantId, 3m, DateTime.UtcNow, DateTime.UtcNow.AddMonths(12), "test", payment, CancellationToken.None)).Should().BeFalse("a replay adds nothing");

        long topped = await StartAsync(wd, credits: credits);
        RunRow run = await ProviderFactory.Run(fixture.ConnectionString).GetByIdAsync(topped, CancellationToken.None);
        run.CreditBudget.Should().Be(3m, "the budget is cut to the plan's credits plus the grant, less what was spent");

        (await grants.RevokeAsync("test", payment, DateTime.UtcNow, CancellationToken.None)).Should().BeTrue();
        (await grants.RevokeAsync("test", payment, DateTime.UtcNow, CancellationToken.None)).Should().BeFalse();
        (await start.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(PlanCreditLimitError);
    }

    [SkippableFact]
    public async Task A_held_grant_does_not_count_until_released_and_a_late_hold_changes_nothing()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int tenantId = NewId();
        int workspaceId = NewId();
        await AddAccountAsync(workspaceId, tenantId);
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);
        await StartAsync(wd, startCost: 5m);
        string payment = $"pay_{Guid.NewGuid():N}";
        ICreditGrantProvider grants = Grants();
        await grants.InsertAsync(tenantId, 3m, DateTime.UtcNow, DateTime.UtcNow.AddMonths(12), "test", payment, CancellationToken.None);
        Func<Task> start = () => StartAsync(wd, credits: new PlanCredits(5m, tenantId));

        (await grants.SetHoldAsync("test", payment, held: true, revision: 1, DateTime.UtcNow, CancellationToken.None)).Should().BeTrue();
        (await start.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(PlanCreditLimitError);
        (await grants.SetHoldAsync("test", payment, held: false, revision: 2, DateTime.UtcNow, CancellationToken.None)).Should().BeTrue();
        (await grants.SetHoldAsync("test", payment, held: true, revision: 1, DateTime.UtcNow, CancellationToken.None)).Should().BeFalse("a hold delivered after its release is stale");
        await start.Should().NotThrowAsync();
        (await grants.SetHoldAsync("test", $"pay_{Guid.NewGuid():N}", held: true, revision: 1, DateTime.UtcNow, CancellationToken.None)).Should().BeFalse("there is no such grant");
    }

    [SkippableFact]
    public async Task An_expired_grant_does_not_count()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int tenantId = NewId();
        int workspaceId = NewId();
        await AddAccountAsync(workspaceId, tenantId);
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);
        long spent = await StartAsync(wd, startCost: 5m);
        await Grants().InsertAsync(tenantId, 100m, new DateTime(2000, 1, 1), new DateTime(2001, 1, 1), "test", $"pay_{Guid.NewGuid():N}", CancellationToken.None);

        Func<Task> start = () => StartAsync(wd, credits: new PlanCredits(5m, tenantId));

        spent.Should().BePositive();
        (await start.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(PlanCreditLimitError);
    }

    [SkippableFact]
    public async Task Credits_posted_past_the_allowance_are_drawn_from_the_soonest_to_expire_grant_as_they_are_posted()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int tenantId = NewId();
        int workspaceId = NewId();
        await AddAccountAsync(workspaceId, tenantId, creditsPerMonth: 10m);
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);
        ICreditGrantProvider grants = Grants();
        string later = $"later_{Guid.NewGuid():N}";
        string sooner = $"sooner_{Guid.NewGuid():N}";
        await grants.InsertAsync(tenantId, 20m, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddMonths(6), "test", later, CancellationToken.None);
        await grants.InsertAsync(tenantId, 10m, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddMonths(3), "test", sooner, CancellationToken.None);
        long run = await StartAsync(wd, startCost: 1m);
        await ChargeAsync(run, 24m);

        await PostAsync();
        await PostAsync();

        (await DrawnAsync(sooner)).Should().Be(10m, "25 posted against an allowance of 10, the sooner grant first");
        (await DrawnAsync(later)).Should().Be(5m);
        (await MonthAsync(tenantId)).Should().Be((25m, 15m), "a second post with nothing new draws nothing");
        long next = await StartAsync(wd, credits: new PlanCredits(10m, tenantId));
        (await ProviderFactory.Run(fixture.ConnectionString).GetByIdAsync(next, CancellationToken.None)).CreditBudget
            .Should().Be(15m, "the plan's 10 are covered and what the grants still hold is left");
    }

    [SkippableFact]
    public async Task A_downgrade_draws_only_what_is_posted_after_it_and_never_from_an_expired_grant()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int tenantId = NewId();
        int workspaceId = NewId();
        await AddAccountAsync(workspaceId, tenantId, creditsPerMonth: 100m);
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);
        string live = $"live_{Guid.NewGuid():N}";
        string expired = $"expired_{Guid.NewGuid():N}";
        await Grants().InsertAsync(tenantId, 50m, DateTime.UtcNow.AddDays(-40), DateTime.UtcNow.AddDays(-1), "test", expired, CancellationToken.None);
        await Grants().InsertAsync(tenantId, 50m, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddMonths(6), "test", live, CancellationToken.None);
        long run = await StartAsync(wd);
        await ChargeAsync(run, 60m);
        await PostAsync();

        (await DrawnAsync(live)).Should().Be(0m, "60 is inside the plan's 100");
        await ExecAsync("UPDATE dbo.WorkspaceAccounts SET CreditsPerMonth = 10 WHERE WorkspaceId = @One", workspaceId, workspaceId);
        await ChargeAsync(run, 5m);
        await PostAsync();

        (await DrawnAsync(live)).Should().Be(5m, "only what was posted after the downgrade, not the 50 already past it");
        (await DrawnAsync(expired)).Should().Be(0m);
        (await MonthAsync(tenantId)).Should().Be((65m, 5m));
    }

    [SkippableFact]
    public async Task A_sub_workflow_child_spends_from_its_parents_budget()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int workspaceId = NewId();
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);
        long parent = await StartAsync(wd, budget: 10m);
        Guid parentRef = (await ProviderFactory.Run(fixture.ConnectionString).GetByIdAsync(parent, CancellationToken.None)).RefId;
        long child = await StartAsync(wd, startCost: 1m, budget: 100m, parentRunRefId: parentRef);
        long grandchild = await StartAsync(wd, budget: 100m, parentRunRefId: (await ProviderFactory.Run(fixture.ConnectionString).GetByIdAsync(child, CancellationToken.None)).RefId);
        ChargeCounter counters = new(ProviderFactory.RunCounters(fixture.ConnectionString));

        (await counters.TryAsync(child, 4m)).Should().Be((5m, 10m), "the child's start cost and charge go on the parent's budget");
        (await counters.TryAsync(grandchild, 3m)).Should().Be((8m, 10m), "a grandchild shares the same root budget");
        (await counters.TryAsync(parent, 2m)).Should().Be((10m, 10m));
        (await counters.TryAsync(child, 1m)).Should().BeNull("the shared budget is spent");
        (await ProviderFactory.Run(fixture.ConnectionString).GetByIdAsync(child, CancellationToken.None)).CreditBudget.Should().Be(10m);
        (await ConsumedAsync(child)).Should().Be(5m, "what the child consumed is still its own, for the ledger");
    }

    [SkippableFact]
    public async Task A_capped_workspaces_runs_lease_their_budgets_so_several_go_at_once_and_the_cap_is_never_passed()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int tenantId = NewId();
        int workspaceId = NewId();
        await AddAccountAsync(workspaceId, tenantId, creditsPerMonth: 1_000m);
        await ExecAsync("INSERT INTO dbo.WorkspaceCreditCaps (WorkspaceId, MonthlyCreditCap) VALUES (@One, 100)", workspaceId, workspaceId);
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);
        var credits = new PlanCredits(1_000m, tenantId);
        var runs = ProviderFactory.Run(fixture.ConnectionString);
        ChargeCounter counters = new(ProviderFactory.RunCounters(fixture.ConnectionString));

        long first = await StartAsync(wd, credits: credits, budget: 1_000m);
        long second = await StartAsync(wd, credits: credits, budget: 1_000m);

        (await runs.GetByIdAsync(first, CancellationToken.None)).CreditBudget.Should().Be(5m, "a run leases a twentieth of the cap");
        (await runs.GetByIdAsync(second, CancellationToken.None)).CreditBudget.Should().Be(5m);
        (await counters.TryAsync(first, 30m)).Should().Be((30m, 100m), "the lease grows by what the charge needs, up to the run's ceiling");
        (await counters.TryAsync(second, 80m)).Should().BeNull("only 65 of the cap is not spent or held by the first run");
        (await runs.GetByIdAsync(second, CancellationToken.None)).CreditBudget.Should().Be(5m, "a refused charge takes no lease");
        (await counters.TryAsync(second, 60m)).Should().Be((60m, 95m));

        long third = await StartAsync(wd, credits: credits, budget: 1_000m);
        (await runs.GetByIdAsync(third, CancellationToken.None)).CreditBudget.Should().Be(5m);
        (await counters.TryAsync(first, 6m)).Should().BeNull("90 is spent and the third run holds the last 5");
        (await counters.TryAsync(third, 5m)).Should().Be((5m, 10m));
        (await counters.TryAsync(third, 5m)).Should().Be((10m, 10m), "the third run takes the last of the cap");
        Func<Task> fourth = () => StartAsync(wd, credits: credits, budget: 1_000m);
        (await fourth.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(WorkspaceCreditCapError);
    }

    [SkippableFact]
    public async Task A_leased_budget_goes_to_its_ceiling_once_the_cap_is_removed()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int tenantId = NewId();
        int workspaceId = NewId();
        await AddAccountAsync(workspaceId, tenantId, creditsPerMonth: 1_000m);
        await ExecAsync("INSERT INTO dbo.WorkspaceCreditCaps (WorkspaceId, MonthlyCreditCap) VALUES (@One, 100)", workspaceId, workspaceId);
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);
        long run = await StartAsync(wd, credits: new PlanCredits(1_000m, tenantId), budget: 50m);
        await ExecAsync("DELETE FROM dbo.WorkspaceCreditCaps WHERE WorkspaceId = @One OR WorkspaceId = @Two", workspaceId, workspaceId);
        ChargeCounter counters = new(ProviderFactory.RunCounters(fixture.ConnectionString));

        (await counters.TryAsync(run, 6m)).Should().Be((6m, 50m));
        (await ProviderFactory.Run(fixture.ConnectionString).GetByIdAsync(run, CancellationToken.None)).CreditBudget.Should().Be(50m);
        (await counters.TryAsync(run, 45m)).Should().BeNull("the run's own budget still holds");
    }

    [SkippableFact]
    public async Task The_reconcile_puts_back_a_grant_whose_row_was_lost_and_a_refund_whose_event_was()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int tenantId = NewId();
        string paymentId = $"pay_{Guid.NewGuid():N}";
        var purchase = new Wbskt.Auth.Models.CreditPurchaseResponse(
            1, tenantId, Guid.NewGuid(), "test", paymentId, 500m, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(364), null, false, 0);
        var auth = new OnePagePurchases(purchase);
        using var metrics = new Wbskt.Management.Host.Telemetry.ManagementMetrics();
        var reconcile = new Wbskt.Management.Host.Services.Billing.CreditGrantReconcile(
            auth, Grants(), metrics, TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<Wbskt.Management.Host.Services.Billing.CreditGrantReconcile>.Instance);

        (await reconcile.ReconcileAsync(CancellationToken.None)).Should().Be(1, "the grant was never recorded");
        (await reconcile.ReconcileAsync(CancellationToken.None)).Should().Be(0);
        await ExecSqlAsync("DELETE FROM dbo.TenantCreditGrants WHERE Source = N'test' AND SourceRef = @Ref", paymentId);
        (await reconcile.ReconcileAsync(CancellationToken.None)).Should().Be(1, "the deleted row comes back");
        (await DrawnAsync(paymentId)).Should().Be(0m);

        auth.Purchase = purchase with { RefundedAt = DateTimeOffset.UtcNow };
        (await reconcile.ReconcileAsync(CancellationToken.None)).Should().Be(1, "the refund's revocation was never delivered");
        (await ScalarSqlAsync("SELECT COUNT(*) FROM dbo.TenantCreditGrants WHERE SourceRef = @Ref AND RevokedAt IS NOT NULL", paymentId)).Should().Be(1);
    }

    private async Task ExecSqlAsync(string sql, string reference)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Ref", reference);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> ScalarSqlAsync(string sql, string reference)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Ref", reference);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private sealed class OnePagePurchases(Wbskt.Auth.Models.CreditPurchaseResponse purchase) : Wbskt.Management.Host.Services.Clients.ICreditPurchasesClient
    {
        public Wbskt.Auth.Models.CreditPurchaseResponse Purchase { get; set; } = purchase;

        public Task<Wbskt.Models.Page<Wbskt.Auth.Models.CreditPurchaseResponse>> GetPageAsync(string? cursor, CancellationToken cancellationToken) =>
            Task.FromResult(new Wbskt.Models.Page<Wbskt.Auth.Models.CreditPurchaseResponse> { Items = [Purchase], NextCursor = null });
    }

    private ICreditGrantProvider Grants() =>
        new CreditGrantProvider(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString })
            .Build());

    [SkippableFact]
    public async Task A_grant_running_out_with_credits_left_is_claimed_once_with_its_balance_and_spent_or_revoked_ones_never()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int tenantId = NewId();
        int workspaceId = NewId();
        await AddAccountAsync(workspaceId, tenantId);
        ICreditGrantProvider grants = Grants();
        string tag = Guid.NewGuid().ToString("N");
        var expires = new DateTime(2090, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        await grants.InsertAsync(tenantId, 100m, DateTime.UtcNow, expires, "test", $"live_{tag}", CancellationToken.None);
        await grants.InsertAsync(tenantId, 100m, DateTime.UtcNow, expires, "test", $"spent_{tag}", CancellationToken.None);
        await grants.InsertAsync(tenantId, 100m, DateTime.UtcNow, expires, "test", $"revoked_{tag}", CancellationToken.None);
        await grants.InsertAsync(tenantId, 100m, DateTime.UtcNow, expires.AddMonths(3), "test", $"later_{tag}", CancellationToken.None);
        await ExecAsync($"UPDATE dbo.TenantCreditGrants SET Drawn = CASE WHEN SourceRef = N'live_{tag}' THEN 40 ELSE 100 END WHERE TenantId = @One AND SourceRef IN (N'live_{tag}', N'spent_{tag}') AND @Two = @Two", tenantId, 0);
        await grants.RevokeAsync("test", $"revoked_{tag}", DateTime.UtcNow, CancellationToken.None);
        var from = new DateTime(2090, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var first = (await grants.ClaimExpiringAsync(from, from.AddDays(30), DateTime.UtcNow, CancellationToken.None)).Where(g => g.TenantId == tenantId).ToList();
        var again = (await grants.ClaimExpiringAsync(from, from.AddDays(30), DateTime.UtcNow, CancellationToken.None)).Where(g => g.TenantId == tenantId).ToList();
        await grants.ReleaseExpiryWarningAsync("test", $"live_{tag}", CancellationToken.None);
        var released = (await grants.ClaimExpiringAsync(from, from.AddDays(30), DateTime.UtcNow, CancellationToken.None)).Where(g => g.TenantId == tenantId).ToList();

        first.Should().ContainSingle().Which.Should().BeEquivalentTo(new { SourceRef = $"live_{tag}", Remaining = 60m, ExpiresAt = expires, WorkspaceIds = new[] { workspaceId } });
        again.Should().BeEmpty();
        released.Should().ContainSingle(g => g.SourceRef == $"live_{tag}");
    }

    private static int NewId() => Random.Shared.Next(1_000_000, int.MaxValue - 1);

    private async Task<long> StartAsync(WorkflowDefinitionRow wd, decimal startCost = 0m, PlanCredits? credits = null, decimal budget = 100m, Guid? parentRunRefId = null)
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
            StartCost = startCost,
            ParentRunRefId = parentRunRefId
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

    private async Task<decimal> DrawnAsync(string sourceRef)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT Drawn FROM dbo.TenantCreditGrants WHERE Source = N'test' AND SourceRef = @Ref", connection);
        command.Parameters.AddWithValue("@Ref", sourceRef);
        return (decimal)(await command.ExecuteScalarAsync())!;
    }

    private async Task PostAsync() =>
        await ProviderFactory.CreditUsage(fixture.ConnectionString).PostAsync(DateOnly.FromDateTime(DateTime.UtcNow), DateTime.UtcNow.AddMinutes(-5), 100_000, CancellationToken.None);

    private async Task<(decimal Posted, decimal Drawn)> MonthAsync(int tenantId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT Posted, Drawn FROM dbo.TenantCreditMonths WHERE TenantId = @TenantId AND MonthStart = DATEFROMPARTS(YEAR(SYSUTCDATETIME()), MONTH(SYSUTCDATETIME()), 1)",
            connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetDecimal(0), reader.GetDecimal(1));
    }

    private async Task<decimal> ConsumedAsync(long runId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT CreditsConsumed FROM dbo.RunCounters WHERE RunId = @RunId", connection);
        command.Parameters.AddWithValue("@RunId", (int)runId);
        return (decimal)(await command.ExecuteScalarAsync())!;
    }

    private sealed class ChargeCounter(Wbskt.Workflow.Providers.RunCountersProvider counters)
    {
        public async Task<(decimal Spent, decimal Budget)?> TryAsync(long runId, decimal cost) =>
            await counters.TryChargeAsync((int)runId, cost, CancellationToken.None) is { } charge ? (charge.Spent, charge.Budget) : null;
    }

    private async Task ExecAsync(string sql, int one, int two)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@One", one);
        command.Parameters.AddWithValue("@Two", two);
        await command.ExecuteNonQueryAsync();
    }

    private async Task AddAccountAsync(int workspaceId, int tenantId, decimal? creditsPerMonth = null)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "INSERT INTO dbo.WorkspaceAccounts (WorkspaceId, TenantId, TenantRefId, PlanCode, PlanRevision, CreditsPerMonth) VALUES (@WorkspaceId, @TenantId, NEWID(), N'Free', 0, @CreditsPerMonth)",
            connection);
        command.Parameters.AddWithValue("@WorkspaceId", workspaceId);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@CreditsPerMonth", (object?)creditsPerMonth ?? DBNull.Value);
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

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
/// monthly credits, a grant is recorded and revoked once per source, and settling a month draws its spend
/// past the plan from the grants, soonest to expire first, once.
/// </summary>
[Collection("SqlEdge")]
public sealed class CreditGrantIntegrationTests(SqlEdgeFixture fixture)
{
    private const int PlanCreditLimitError = 50033;
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
    public async Task Settling_a_month_draws_its_overage_from_the_soonest_to_expire_grant_first_and_only_once()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int tenantId = NewId();
        int one = NewId();
        int two = NewId();
        await AddAccountAsync(one, tenantId);
        await AddAccountAsync(two, tenantId);
        var month = new DateOnly(2001, 1, 1);
        await ExecAsync("""
            INSERT INTO dbo.WorkspaceCreditUsage (WorkspaceId, Day, Credits)
            VALUES (@One, '2001-01-03', 15), (@Two, '2001-01-20', 10), (@One, '2001-02-01', 1000);
            """, one, two);
        ICreditGrantProvider grants = Grants();
        string later = $"later_{Guid.NewGuid():N}";
        string sooner = $"sooner_{Guid.NewGuid():N}";
        await grants.InsertAsync(tenantId, 20m, new DateTime(2000, 12, 1), new DateTime(2001, 6, 1), "test", later, CancellationToken.None);
        await grants.InsertAsync(tenantId, 10m, new DateTime(2000, 12, 1), new DateTime(2001, 3, 1), "test", sooner, CancellationToken.None);

        (await grants.GetUnsettledAsync(month, CancellationToken.None)).Should().Contain(t => t.TenantId == tenantId && t.PlanCode == PlanCode.Free);
        decimal? drawn = await grants.SettleAsync(tenantId, month, 10m, CancellationToken.None);
        decimal? again = await grants.SettleAsync(tenantId, month, 10m, CancellationToken.None);

        drawn.Should().Be(15m, "25 spent in January against an allowance of 10");
        again.Should().BeNull();
        (await DrawnAsync(sooner)).Should().Be(10m);
        (await DrawnAsync(later)).Should().Be(5m);
        (await grants.GetUnsettledAsync(month, CancellationToken.None)).Should().NotContain(t => t.TenantId == tenantId);
    }

    [SkippableFact]
    public async Task A_month_within_its_allowance_draws_nothing()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int tenantId = NewId();
        int workspaceId = NewId();
        await AddAccountAsync(workspaceId, tenantId);
        await ExecAsync("INSERT INTO dbo.WorkspaceCreditUsage (WorkspaceId, Day, Credits) VALUES (@One, '2001-01-03', 4)", workspaceId, workspaceId);
        string payment = $"pay_{Guid.NewGuid():N}";
        await Grants().InsertAsync(tenantId, 20m, new DateTime(2000, 12, 1), new DateTime(2001, 6, 1), "test", payment, CancellationToken.None);

        decimal? drawn = await Grants().SettleAsync(tenantId, new DateOnly(2001, 1, 1), 10m, CancellationToken.None);

        drawn.Should().Be(0m);
        (await DrawnAsync(payment)).Should().Be(0m);
    }

    private ICreditGrantProvider Grants() =>
        new CreditGrantProvider(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString })
            .Build());

    private static int NewId() => Random.Shared.Next(1_000_000, int.MaxValue - 1);

    private async Task<long> StartAsync(WorkflowDefinitionRow wd, decimal startCost = 0m, PlanCredits? credits = null)
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
            CreditBudget = 100m,
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

    private async Task<decimal> DrawnAsync(string sourceRef)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT Drawn FROM dbo.TenantCreditGrants WHERE Source = N'test' AND SourceRef = @Ref", connection);
        command.Parameters.AddWithValue("@Ref", sourceRef);
        return (decimal)(await command.ExecuteScalarAsync())!;
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

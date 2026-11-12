using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Wbskt.Infrastructure.Billing;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Billing;
using Wbskt.Models.Billing;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// Business runs past its monthly credits (#701): dbo.Run_Start holds a Business account to twice its
/// credits, and dbo.BusinessUsage_GetBy_Month reads what each Business tenant used in a month for the
/// overage invoice, leaving out what its credit packs paid for. Months are closed once (#742), so each test
/// reports on a month of its own, far in the future, with the clock set to just after it.
/// </summary>
[Collection("SqlEdge")]
public sealed class BusinessOverageIntegrationTests(SqlEdgeFixture fixture)
{
    private const int PlanCreditLimitError = 50033;
    private const string SkipReason = "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.";

    [SkippableFact]
    public async Task A_business_account_at_one_and_a_half_times_its_credits_starts_a_run_and_at_twice_cannot()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        int tenantId = NewId();
        int workspaceId = NewId();
        await AddAccountAsync(workspaceId, tenantId, "Business", null, creditsPerMonth: 10m);
        WorkflowDefinitionRow wd = await CreateDefinitionAsync(workspaceId);
        PlanCredits credits = new WorkspacePlan(workspaceId, tenantId, PlanCode.Business, PlanCatalog.BuiltIn[PlanCode.Business] with { CreditsPerMonth = 10m }).Credits;
        long spender = await StartAsync(wd, credits);

        await ChargeAsync(spender, 15m);
        await PostAsync();
        long past = await StartAsync(wd, credits);
        await ChargeAsync(spender, 5m);
        await PostAsync();
        Func<Task> atTwice = () => StartAsync(wd, credits);

        past.Should().BePositive();
        (await atTwice.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(PlanCreditLimitError);
    }

    [SkippableFact]
    public async Task The_report_bills_a_business_tenants_month_past_its_contract_and_not_what_its_packs_paid_for()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        DateOnly month = NewMonth();
        int business = NewId();
        int pro = NewId();
        int first = NewId();
        int second = NewId();
        int proWorkspace = NewId();
        const string Contract = """{"messagesPerMonth":1000,"creditsPerMonth":100}""";
        await AddAccountAsync(first, business, "Business", Contract, 100m);
        await AddAccountAsync(second, business, "Business", Contract, 100m);
        await AddAccountAsync(proWorkspace, pro, "Pro", null, 150_000m);
        await AddMessagesAsync(first, month, 600);
        await AddMessagesAsync(second, month.AddDays(30), 900);
        await AddMessagesAsync(second, month.AddMonths(1), 5_000);
        await AddMessagesAsync(proWorkspace, month, 999_999_999);
        await AddCreditMonthAsync(business, month, posted: 180m, drawn: 30m);

        Guid businessRef = await TenantRefAsync(first);
        Guid proRef = await TenantRefAsync(proWorkspace);

        var report = await Service(ClosedAt(month)).GetAsync(month.Year, month.Month, CancellationToken.None);

        report.Value.Closed.Should().BeTrue();
        var bill = report.Value.Tenants.Should().ContainSingle(t => t.TenantRefId == businessRef).Subject;
        (bill.MessagesUsed, bill.ExtraMessages).Should().Be((1_500L, 500L));
        (bill.CreditsUsed, bill.CreditsFromPacks, bill.ExtraCredits).Should().Be((180m, 30m, 50m));
        bill.AmountUsd.Should().Be(0.03m);
        report.Value.Tenants.Should().NotContain(t => t.TenantRefId == proRef);
    }

    [SkippableFact]
    public async Task A_contract_that_ends_in_the_month_is_billed_for_it_and_a_closed_month_does_not_change()
    {
        Skip.IfNot(fixture.IsAvailable, SkipReason);

        DateOnly month = NewMonth();
        int ended = NewId();
        int endedWorkspace = NewId();
        int deletedWorkspace = NewId();
        int later = NewId();
        int laterWorkspace = NewId();
        const string Contract = """{"messagesPerMonth":1000,"creditsPerMonth":100}""";
        await AddAccountAsync(endedWorkspace, ended, "Business", Contract, 100m);
        await AddAccountAsync(deletedWorkspace, ended, "Business", Contract, 100m);
        await AddMessagesAsync(endedWorkspace, month, 700);
        await AddMessagesAsync(deletedWorkspace, month.AddDays(1), 800);
        await AddCreditMonthAsync(ended, month, posted: 160m, drawn: 0m);
        Guid endedRef = await TenantRefAsync(endedWorkspace);

        // Seen on Business during the month; then a workspace is deleted and the contract ends.
        await Service(month.AddDays(10)).TrackAsync(CancellationToken.None);
        await ExecuteAsync("UPDATE dbo.WorkspaceAccounts SET RetiredAt = SYSUTCDATETIME() WHERE WorkspaceId = @p0;", deletedWorkspace);
        await ExecuteAsync("UPDATE dbo.WorkspaceAccounts SET PlanCode = N'Free', LimitOverrides = NULL, PlanRevision = 2 WHERE WorkspaceId = @p0;", endedWorkspace);

        // A tenant whose copy moved to Business only after the month ended is not in it.
        await AddAccountAsync(laterWorkspace, later, "Business", null, 100m);
        await ExecuteAsync("UPDATE dbo.WorkspaceAccounts SET UpdatedAt = @p1 WHERE WorkspaceId = @p0;", laterWorkspace, month.AddMonths(1).AddDays(1).ToDateTime(TimeOnly.MinValue));
        Guid laterRef = await TenantRefAsync(laterWorkspace);

        var report = await Service(ClosedAt(month)).GetAsync(month.Year, month.Month, CancellationToken.None);
        await AddMessagesAsync(endedWorkspace, month.AddDays(2), 50_000);
        var again = await Service(ClosedAt(month).AddDays(5)).GetAsync(month.Year, month.Month, CancellationToken.None);

        var bill = report.Value.Tenants.Should().ContainSingle(t => t.TenantRefId == endedRef).Subject;
        (bill.MessagesPerMonth, bill.MessagesUsed, bill.ExtraMessages).Should().Be((1_000L, 1_500L, 500L), "the contract as last seen, and the deleted workspace's messages too");
        bill.ExtraCredits.Should().Be(60m);
        report.Value.Tenants.Should().NotContain(t => t.TenantRefId == laterRef);
        again.Value.Should().BeEquivalentTo(report.Value, "a closed month is read as it was closed");
    }

    private BusinessOverageService Service(DateOnly day) => Service(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

    private BusinessOverageService Service(DateTime now) => new(
        new BusinessUsageProvider(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString })
            .Build()),
        new PlanCatalog(Options.Create(new PlanCatalogOptions())),
        new FixedClock(new DateTimeOffset(now, TimeSpan.Zero)),
        Options.Create(new Wbskt.Management.Host.Models.BillingOptions()));

    // A month of this test's own, so closing it touches no other test's.
    private static DateOnly NewMonth() => new(Random.Shared.Next(3000, 9000), Random.Shared.Next(1, 13), 1);

    private static DateTime ClosedAt(DateOnly month) => month.AddMonths(1).ToDateTime(new TimeOnly(3, 0), DateTimeKind.Utc);

    private static int NewId() => Random.Shared.Next(1_000_000, int.MaxValue - 1);

    private async Task<long> StartAsync(WorkflowDefinitionRow wd, PlanCredits credits)
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
            StartCost = 0m
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

    private Task PostAsync() =>
        ProviderFactory.CreditUsage(fixture.ConnectionString).PostAsync(DateOnly.FromDateTime(DateTime.UtcNow), 500, CancellationToken.None);

    private Task AddAccountAsync(int workspaceId, int tenantId, string plan, string? overrides, decimal creditsPerMonth) =>
        ExecuteAsync("""
            INSERT INTO dbo.WorkspaceAccounts (WorkspaceId, TenantId, TenantRefId, PlanCode, LimitOverrides, PlanRevision, CreditsPerMonth)
            VALUES (@p0, @p1, ISNULL((SELECT TOP 1 TenantRefId FROM dbo.WorkspaceAccounts WHERE TenantId = @p1), NEWID()), @p2, @p3, 1, @p4);
            """, workspaceId, tenantId, plan, (object?)overrides ?? DBNull.Value, creditsPerMonth);

    private Task AddMessagesAsync(int workspaceId, DateOnly day, long inbound) =>
        ExecuteAsync("INSERT INTO dbo.WorkspaceMessageUsage (WorkspaceId, Day, Inbound, Outbound) VALUES (@p0, @p1, @p2, 0);",
            workspaceId, day.ToDateTime(TimeOnly.MinValue), inbound);

    private Task AddCreditMonthAsync(int tenantId, DateOnly month, decimal posted, decimal drawn) =>
        ExecuteAsync("INSERT INTO dbo.TenantCreditMonths (TenantId, MonthStart, Posted, Drawn) VALUES (@p0, @p1, @p2, @p3);",
            tenantId, month.ToDateTime(TimeOnly.MinValue), posted, drawn);

    private async Task<Guid> TenantRefAsync(int workspaceId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT TenantRefId FROM dbo.WorkspaceAccounts WHERE WorkspaceId = @p0", connection);
        command.Parameters.AddWithValue("@p0", workspaceId);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExecuteAsync(string sql, params object[] args)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        for (int i = 0; i < args.Length; i++)
        {
            command.Parameters.AddWithValue($"@p{i}", args[i]);
        }

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

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

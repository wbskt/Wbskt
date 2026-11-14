using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Services;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.StaffActions;
using Wbskt.Models.Admin;
using Wbskt.Models.Billing;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.StaffActions;

/// <summary>
/// A staff plan or limits change against a real auth database, through the real plan service and the
/// staff runner: the plan, its outbox event and the staff audit row commit together, and a stale change
/// leaves the tenant as it was with only a refused row.
/// </summary>
[Collection("SqlEdge")]
public sealed class AdminTenantPlanSqlTests(AuthSqlFixture auth)
{
    private const string Skipped = "SQL Server not reachable — skipping.";

    private AdminTenantService Service()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:AuthDBConnection"] = auth.ConnectionString })
            .Build();
        var plans = new TenantPlanService(
            new TenantPlanProvider(configuration),
            new AuditWorkspaces(new WorkspaceProvider(configuration), NullLogger<AuditWorkspaces>.Instance),
            NullLogger<TenantPlanService>.Instance);
        var runner = new StaffActionRunner(new StaffAuditProvider(configuration, "AuthDBConnection"), NullLogger<StaffActionRunner>.Instance);
        return new AdminTenantService(plans, new UnusedLimits(), runner, TimeProvider.System);
    }

    private static StaffChange Staff(Guid key) => new(Guid.NewGuid(), "pika", "Integration test of a staff plan", key);

    [SkippableFact]
    public async Task A_staff_plan_commits_with_its_event_and_its_audit_row()
    {
        Skip.IfNot(auth.IsAvailable, Skipped);
        var tenantRef = await CreateTenantAsync();
        var key = Guid.NewGuid();

        var result = await Service().SetPlanAsync(
            tenantRef,
            new AdminSetTenantPlanRequest(Staff(key), PlanCode.Pro, new PlanLimitOverrides { Clients = 900 }, 0, DateTimeOffset.UtcNow.AddDays(30)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.Tenants WHERE RefId = @Id AND PlanCode = N'Pro' AND PlanSource = N'Operator' AND PlanRevision = 1 AND PlanEndsAt IS NOT NULL;", tenantRef));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.OutboxMessages WHERE Payload LIKE N'%' + CONVERT(NVARCHAR(36), @Id) + N'%';", tenantRef));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.StaffAudit WHERE IdempotencyKey = @Id AND Outcome = N'Succeeded' AND Action = N'SetTenantPlan';", key));
    }

    [SkippableFact]
    public async Task A_limits_change_keeps_the_plan_and_its_source()
    {
        Skip.IfNot(auth.IsAvailable, Skipped);
        var tenantRef = await CreateTenantAsync();

        var result = await Service().SetLimitsAsync(
            tenantRef, new AdminSetTenantLimitsRequest(Staff(Guid.NewGuid()), new PlanLimitOverrides { Clients = 40 }, 0), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.Tenants WHERE RefId = @Id AND PlanCode = N'Free' AND PlanSource = N'Default' AND PlanLimitOverrides IS NOT NULL AND PlanRevision = 1;", tenantRef));
    }

    [SkippableFact]
    public async Task A_stale_change_leaves_the_tenant_and_only_a_refused_row()
    {
        Skip.IfNot(auth.IsAvailable, Skipped);
        var tenantRef = await CreateTenantAsync();
        var key = Guid.NewGuid();

        var result = await Service().SetLimitsAsync(
            tenantRef, new AdminSetTenantLimitsRequest(Staff(key), new PlanLimitOverrides { Clients = 40 }, 5), CancellationToken.None);

        Assert.Equal("TENANT_PLAN_CHANGED", result.Error.Code);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.Tenants WHERE RefId = @Id AND PlanRevision = 0 AND PlanLimitOverrides IS NULL;", tenantRef));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.OutboxMessages WHERE Payload LIKE N'%' + CONVERT(NVARCHAR(36), @Id) + N'%';", tenantRef));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.StaffAudit WHERE IdempotencyKey = @Id AND Outcome = N'Refused' AND Detail = N'TENANT_PLAN_CHANGED';", key));
    }

    private async Task<Guid> CreateTenantAsync()
    {
        var tenantRef = Guid.NewGuid();
        await using var conn = new SqlConnection(auth.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("INSERT INTO dbo.Tenants (RefId, Name) VALUES (@Id, @Name);", conn);
        cmd.Parameters.AddWithValue("@Id", tenantRef);
        cmd.Parameters.AddWithValue("@Name", $"staff-{Guid.NewGuid():N}"[..30]);
        await cmd.ExecuteNonQueryAsync();
        return tenantRef;
    }

    private async Task<int> CountAsync(string sql, Guid id)
    {
        await using var conn = new SqlConnection(auth.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", id);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>The limits are only read by the GET, which these tests do not call.</summary>
    private sealed class UnusedLimits : ITenantLimits
    {
        public Task<PlanLimits> ForTenantAsync(int tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PlanLimits> ForTenantAsync(Guid tenantRef, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Error> WithSeatsAsync(Error refusal, Guid tenantRef, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Error> WithSeatsAsync(Error refusal, int tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

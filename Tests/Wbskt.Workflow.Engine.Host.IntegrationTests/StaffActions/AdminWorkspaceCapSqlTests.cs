using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Wbskt.Auth.Models;
using Wbskt.EventBus.Abstractions;
using Wbskt.Infrastructure.StaffActions;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Admin;
using Wbskt.Management.Host.Services.Billing;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Models;
using Wbskt.Models;
using Wbskt.Models.Admin;
using Wbskt.Models.Billing;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.StaffActions;

/// <summary>
/// A staff change to a workspace's credit cap against a real main database, through the console's cap
/// service and the staff runner: dbo.WorkspaceCreditCap_Set runs its own transaction inside the runner's,
/// and the cap and the staff audit row commit together, once per idempotency key.
/// </summary>
[Collection("SqlEdge")]
public sealed class AdminWorkspaceCapSqlTests(SqlEdgeFixture main)
{
    private const string Skipped = "SQL Server not reachable — skipping.";

    private static readonly Guid WorkspaceRef = Guid.NewGuid();

    private AdminWorkspaceService Service(int workspaceId)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = main.ConnectionString })
            .Build();
        var runner = new StaffActionRunner(new StaffAuditProvider(configuration, "DefaultConnection"), NullLogger<StaffActionRunner>.Instance);
        return new AdminWorkspaceService(new OneWorkspace(workspaceId), new WorkspaceCreditCapProvider(configuration), new NoBus(), TimeProvider.System,
            runner, NullLogger<AdminWorkspaceService>.Instance);
    }

    private static StaffChange Staff(Guid key) => new(Guid.NewGuid(), "pika", "Integration test of a workspace cap", key);

    [SkippableFact]
    public async Task A_cap_commits_with_its_audit_row_and_a_retry_changes_nothing()
    {
        Skip.IfNot(main.IsAvailable, Skipped);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        var key = Guid.NewGuid();

        var first = await Service(workspaceId).SetCreditCapAsync(WorkspaceRef, new AdminSetWorkspaceCreditCapRequest(Staff(key), 750m), CancellationToken.None);
        var retry = await Service(workspaceId).SetCreditCapAsync(WorkspaceRef, new AdminSetWorkspaceCreditCapRequest(Staff(key), 750m), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.False(first.Value.Replayed);
        Assert.True(retry.Value.Replayed);
        Assert.Equal(first.Value.AuditId, retry.Value.AuditId);
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.WorkspaceCreditCaps WHERE WorkspaceId = {workspaceId} AND MonthlyCreditCap = 750;"));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.StaffAudit WHERE IdempotencyKey = '{key}' AND Outcome = N'Succeeded' AND Action = N'SetWorkspaceCreditCap' AND TargetKind = N'Workspace';"));
    }

    [SkippableFact]
    public async Task Clearing_a_cap_removes_it()
    {
        Skip.IfNot(main.IsAvailable, Skipped);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);

        await Service(workspaceId).SetCreditCapAsync(WorkspaceRef, new AdminSetWorkspaceCreditCapRequest(Staff(Guid.NewGuid()), 750m), CancellationToken.None);
        var cleared = await Service(workspaceId).SetCreditCapAsync(WorkspaceRef, new AdminSetWorkspaceCreditCapRequest(Staff(Guid.NewGuid()), null), CancellationToken.None);

        Assert.True(cleared.IsSuccess);
        Assert.Equal(0, await CountAsync($"SELECT COUNT(*) FROM dbo.WorkspaceCreditCaps WHERE WorkspaceId = {workspaceId};"));
    }

    [SkippableFact]
    public async Task An_invalid_cap_leaves_only_a_refused_row()
    {
        Skip.IfNot(main.IsAvailable, Skipped);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        var key = Guid.NewGuid();

        var result = await Service(workspaceId).SetCreditCapAsync(WorkspaceRef, new AdminSetWorkspaceCreditCapRequest(Staff(key), -1m), CancellationToken.None);

        Assert.Equal("WORKSPACE_CREDIT_CAP_INVALID", result.Error.Code);
        Assert.Equal(0, await CountAsync($"SELECT COUNT(*) FROM dbo.WorkspaceCreditCaps WHERE WorkspaceId = {workspaceId};"));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.StaffAudit WHERE IdempotencyKey = '{key}' AND Outcome = N'Refused';"));
    }

    private async Task<int> CountAsync(string sql)
    {
        await using var connection = new SqlConnection(main.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private sealed class OneWorkspace(int workspaceId) : IWorkspaceAccountsClient
    {
        public Task<Page<WorkspaceAccountResponse>> GetPageAsync(string? cursor, int? tenantId, int? workspaceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WorkspaceAccountResponse?> FindAsync(Guid workspaceRef, CancellationToken cancellationToken) =>
            Task.FromResult<WorkspaceAccountResponse?>(new WorkspaceAccountResponse(workspaceId, 1, Guid.NewGuid(), PlanCode.Pro, null, 1));
    }

    private sealed class NoBus : IEventBus
    {
        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : Wbskt.EventBus.Abstractions.IEvent => Task.CompletedTask;
    }
}

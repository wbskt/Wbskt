using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Management.Host.Providers;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// <c>dbo.Workspace_Retire</c>, which runs when a workspace is deleted in the auth database. Nothing in
/// the main database can cascade from that, so this is the only thing standing between a deleted
/// workspace and its PIN still registering devices, its devices still connecting, and its schedules
/// and webhooks still starting runs.
/// </summary>
[Collection("SqlEdge")]
public sealed class WorkspaceRetirementIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";

    [SkippableFact]
    public async Task Retiring_a_workspace_shuts_down_everything_it_owns()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var ws = await SeedWorkspaceAsync();

        var retired = await Provider().RetireAsync(ws.WorkspaceId);

        Assert.False(await ScalarAsync<bool>("SELECT IsEnabled FROM dbo.RegistrationPolicies WHERE Id = @p0", ws.PolicyId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Clients WHERE WorkspaceId = @p0 AND Status IN (0, 1)", ws.WorkspaceId));
        Assert.Equal(3, await ScalarAsync<byte>("SELECT Status FROM dbo.Clients WHERE Id = @p0", ws.RejectedClientId));
        Assert.False(await ScalarAsync<bool>("SELECT IsEnabled FROM dbo.WorkflowDefinitions WHERE Id = @p0", ws.DefinitionId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.TriggerRegistrations WHERE WorkflowDefinitionId = @p0", ws.DefinitionId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ScheduledFires WHERE WorkflowDefinitionId = @p0", ws.DefinitionId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PendingTriggerEvents WHERE WorkflowRefId = @p0", ws.WorkflowRefId));

        // What the handler must still do outside the database.
        Assert.Equal([ws.PendingClientId, ws.RegisteredClientId], retired.RevokedClients.Select(c => c.ClientId).Order());
        Assert.All(retired.RevokedClients, c => Assert.Equal(ws.PolicyRefId, c.PolicyRefId));
        Assert.Equal([ws.RunningRunId], retired.ActiveRunIds);
        Assert.Equal([ws.DefinitionId], retired.DefinitionIds);

        // The neighbouring workspace is untouched.
        Assert.True(await ScalarAsync<bool>("SELECT IsEnabled FROM dbo.RegistrationPolicies WHERE Id = @p0", ws.OtherPolicyId));
        Assert.Equal(1, await ScalarAsync<byte>("SELECT Status FROM dbo.Clients WHERE Id = @p0", ws.OtherClientId));
    }

    /// <summary>
    /// The event behind this can be delivered twice. The second run must not report the clients
    /// again, or every socket host is told to drop them twice and the event log shows two revocations.
    /// </summary>
    [SkippableFact]
    public async Task Retiring_twice_reports_each_client_once()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var ws = await SeedWorkspaceAsync();

        await Provider().RetireAsync(ws.WorkspaceId);
        var second = await Provider().RetireAsync(ws.WorkspaceId);

        Assert.Empty(second.RevokedClients);
    }

    // ---------------------------------------------------------------- helpers

    private sealed record SeededWorkspace(
        int WorkspaceId, int PolicyId, Guid PolicyRefId,
        int PendingClientId, int RegisteredClientId, int RejectedClientId,
        int DefinitionId, Guid WorkflowRefId, long RunningRunId,
        int OtherPolicyId, int OtherClientId);

    private IWorkspaceRetirementProvider Provider() =>
        new WorkspaceRetirementProvider(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString })
            .Build());

    private async Task<SeededWorkspace> SeedWorkspaceAsync()
    {
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        int otherWorkspaceId = workspaceId - 1;

        var (policyId, policyRefId) = await CreatePolicyAsync(workspaceId);
        var (otherPolicyId, _) = await CreatePolicyAsync(otherWorkspaceId);

        int pending = await CreateClientAsync(workspaceId, policyId, status: 0);
        int registered = await CreateClientAsync(workspaceId, policyId, status: 1);
        int rejected = await CreateClientAsync(workspaceId, policyId, status: 3);
        int otherClient = await CreateClientAsync(otherWorkspaceId, otherPolicyId, status: 1);

        Guid workflowRefId = Guid.NewGuid();
        Guid triggerNodeId = Guid.NewGuid();
        int definitionId = await InsertAsync<int>("""
            INSERT INTO dbo.WorkflowDefinitions (RefId, Version, WorkspaceId, Name, DefinitionJson, PublishedBy)
            OUTPUT INSERTED.Id VALUES (@p0, 1, @p1, 'retire-test', '{}', 1);
            """, workflowRefId, workspaceId);

        await ExecAsync("""
            INSERT INTO dbo.TriggerRegistrations (WorkflowDefinitionId, WorkflowRefId, WorkflowVersion, TriggerNodeId, TriggerKind, TriggerKey)
            VALUES (@p0, @p1, 1, @p2, 'webhook', CONCAT('webhook:retire:', @p1));
            INSERT INTO dbo.ScheduledFires (TriggerNodeId, WorkflowDefinitionId, WorkflowRefId, CronOrInterval, NextFireAt)
            VALUES (@p2, @p0, @p1, '* * * * *', SYSUTCDATETIME());
            INSERT INTO dbo.PendingTriggerEvents (WorkflowRefId, TriggerNodeId, CorrelationKey, InboundEventJson)
            VALUES (@p1, @p2, 'k', '{}');
            INSERT INTO dbo.Runs (RefId, WorkflowDefinitionId, WorkflowRefId, WorkflowVersion, TriggerNodeId, Status, StartedAt, CompletedAt, CreditBudget)
            VALUES (NEWID(), @p0, @p1, 1, @p2, N'Completed', SYSUTCDATETIME(), SYSUTCDATETIME(), 0);
            """, definitionId, workflowRefId, triggerNodeId);

        long runningRunId = await InsertAsync<int>("""
            INSERT INTO dbo.Runs (RefId, WorkflowDefinitionId, WorkflowRefId, WorkflowVersion, TriggerNodeId, Status, StartedAt, CreditBudget)
            OUTPUT INSERTED.Id VALUES (NEWID(), @p0, @p1, 1, @p2, N'Running', SYSUTCDATETIME(), 0);
            """, definitionId, workflowRefId, triggerNodeId);

        return new SeededWorkspace(workspaceId, policyId, policyRefId, pending, registered, rejected,
            definitionId, workflowRefId, runningRunId, otherPolicyId, otherClient);
    }

    private async Task<(int Id, Guid RefId)> CreatePolicyAsync(int workspaceId)
    {
        Guid refId = Guid.NewGuid();
        int id = await InsertAsync<int>("""
            INSERT INTO dbo.RegistrationPolicies (RefId, WorkspaceId, Pin, Name)
            OUTPUT INSERTED.Id VALUES (@p0, @p1, @p2, 'retire-test');
            """, refId, workspaceId, Guid.NewGuid().ToString("N")[..20]);
        return (id, refId);
    }

    private Task<int> CreateClientAsync(int workspaceId, int policyId, byte status) =>
        InsertAsync<int>("""
            INSERT INTO dbo.Clients (WorkspaceId, PolicyId, Name, SecretHash, Status)
            OUTPUT INSERTED.Id VALUES (@p0, @p1, 'retire-test', 0x00, @p2);
            """, workspaceId, policyId, status);

    private async Task<T> InsertAsync<T>(string sql, params object[] args) => (T)(await RunAsync(sql, args, scalar: true))!;

    private Task ExecAsync(string sql, params object[] args) => RunAsync(sql, args, scalar: false);

    private async Task<T> ScalarAsync<T>(string sql, params object[] args) => (T)(await RunAsync(sql, args, scalar: true))!;

    private async Task<object?> RunAsync(string sql, object[] args, bool scalar)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < args.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        }

        return scalar ? await cmd.ExecuteScalarAsync() : await cmd.ExecuteNonQueryAsync();
    }
}

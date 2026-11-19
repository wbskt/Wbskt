using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Management.Host.Providers;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// <c>dbo.Workspace_Retire</c>, which runs when a workspace is deleted in the auth database. Nothing in
/// the main database can cascade from that, so this is the only thing standing between a deleted
/// workspace and its PIN still registering clients, its clients still connecting, and its schedules
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
        Assert.Equal(2, await ScalarAsync<byte>("SELECT State FROM dbo.Workflows WHERE RefId = @p0 AND LiveDefinitionId IS NULL", ws.WorkflowRefId));
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
    /// A retry after announcing the revoked clients failed part-way must reach the ones after the
    /// failure, so a second run reports every revoked client again, and still only revoked ones.
    /// </summary>
    [SkippableFact]
    public async Task Retiring_again_reports_the_revoked_clients_again()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var ws = await SeedWorkspaceAsync();

        await Provider().RetireAsync(ws.WorkspaceId);
        var second = await Provider().RetireAsync(ws.WorkspaceId);

        Assert.Equal([ws.PendingClientId, ws.RegisteredClientId], second.RevokedClients.Select(c => c.ClientId).Order());
    }

    /// <summary>
    /// The deleted workspace sweep retires, and counts, only a workspace this answers yes for, so it
    /// must answer no once a retirement has finished and yes for anything retiring would still change.
    /// </summary>
    [SkippableFact]
    public async Task A_workspace_has_live_content_until_it_is_retired_and_its_runs_end()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var ws = await SeedWorkspaceAsync();

        Assert.True(await Provider().HasLiveContentAsync(ws.WorkspaceId));

        await Provider().RetireAsync(ws.WorkspaceId);

        // The run the retirement asked the engine to cancel is still going, so it still counts.
        Assert.True(await Provider().HasLiveContentAsync(ws.WorkspaceId));

        await ExecAsync("UPDATE dbo.Runs SET Status = N'Cancelled', CompletedAt = SYSUTCDATETIME() WHERE Id = @p0", ws.RunningRunId);

        Assert.False(await Provider().HasLiveContentAsync(ws.WorkspaceId));

        // The neighbouring workspace is still live.
        Assert.True(await Provider().HasLiveContentAsync(ws.WorkspaceId - 1));
    }

    [SkippableFact]
    public async Task A_workspace_with_nothing_here_has_no_live_content()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);

        Assert.False(await Provider().HasLiveContentAsync(Random.Shared.Next(1_000_000, int.MaxValue)));
    }

    [SkippableFact]
    public async Task Each_thing_retiring_would_change_counts_as_live_on_its_own()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);

        // An enabled policy and nothing else.
        int policyOnly = Random.Shared.Next(1_000_000, int.MaxValue);
        await CreatePolicyAsync(policyOnly);
        Assert.True(await Provider().HasLiveContentAsync(policyOnly));

        // A registered client under a policy that is already disabled.
        int clientOnly = Random.Shared.Next(1_000_000, int.MaxValue);
        var (policyId, _) = await CreatePolicyAsync(clientOnly);
        await ExecAsync("UPDATE dbo.RegistrationPolicies SET IsEnabled = 0 WHERE Id = @p0", policyId);
        int clientId = await CreateClientAsync(clientOnly, policyId, status: 1);
        Assert.True(await Provider().HasLiveContentAsync(clientOnly));
        await ExecAsync("UPDATE dbo.Clients SET Status = 2 WHERE Id = @p0", clientId);
        Assert.False(await Provider().HasLiveContentAsync(clientOnly));

        // An enabled workflow and nothing else.
        int workflowOnly = Random.Shared.Next(1_000_000, int.MaxValue);
        int definitionId = await InsertAsync<int>("""
            INSERT INTO dbo.WorkflowDefinitions (RefId, Version, WorkspaceId, Name, DefinitionJson, PublishedBy)
            OUTPUT INSERTED.Id VALUES (NEWID(), 1, @p0, 'live-test', '{}', 1);
            """, workflowOnly);
        await ExecAsync("""
            INSERT INTO dbo.Workflows (RefId, WorkspaceId, State, LiveDefinitionId, StateChangedBy)
            SELECT RefId, WorkspaceId, 1, Id, 1 FROM dbo.WorkflowDefinitions WHERE Id = @p0;
            """, definitionId);
        Assert.True(await Provider().HasLiveContentAsync(workflowOnly));
        await ExecAsync("UPDATE W SET State = 2, LiveDefinitionId = NULL FROM dbo.Workflows W JOIN dbo.WorkflowDefinitions D ON D.RefId = W.RefId WHERE D.Id = @p0", definitionId);
        Assert.False(await Provider().HasLiveContentAsync(workflowOnly));
    }

    /// <summary>
    /// #803: a deleted workspace's account copy goes once nothing reads it: it was retired before last
    /// month, the tenant's month no longer sums its usage, every month it sent messages in is closed, and
    /// no run or reading is left for the retention sweeps to find through it. Any one of those keeps it.
    /// </summary>
    [SkippableFact]
    public async Task A_retired_account_copy_is_pruned_once_nothing_reads_it()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var now = new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);
        var longAgo = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        await ExecAsync("""
            IF NOT EXISTS (SELECT 1 FROM dbo.BusinessMonths WHERE MonthStart = '2019-05-01')
                INSERT INTO dbo.BusinessMonths (MonthStart, TrackedFrom, ClosedAt) VALUES ('2019-05-01', '2019-05-01', NULL);
            """);

        int unread = await CreateAccountAsync(longAgo);
        int justBeforeLastMonth = await CreateAccountAsync(new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc));
        int atLastMonth = await CreateAccountAsync(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
        int live = await CreateAccountAsync(retiredAt: null);

        int creditsLastMonth = await CreateAccountAsync(longAgo);
        await ExecAsync("INSERT INTO dbo.WorkspaceCreditUsage (WorkspaceId, Day, Credits) VALUES (@p0, '2026-10-31', 1)", creditsLastMonth);

        int creditsBeforeThat = await CreateAccountAsync(longAgo);
        await ExecAsync("INSERT INTO dbo.WorkspaceCreditUsage (WorkspaceId, Day, Credits) VALUES (@p0, '2026-09-30', 1)", creditsBeforeThat);

        int messagesLastMonth = await CreateAccountAsync(longAgo);
        await ExecAsync("INSERT INTO dbo.WorkspaceMessageUsage (WorkspaceId, Day, Inbound, Outbound) VALUES (@p0, '2026-10-01', 1, 0)", messagesLastMonth);

        // A month not closed is summed live for the Business invoice, but only for tenants on Business.
        int oldMessagesNotBusiness = await CreateAccountAsync(longAgo);
        await ExecAsync("INSERT INTO dbo.WorkspaceMessageUsage (WorkspaceId, Day, Inbound, Outbound) VALUES (@p0, '2019-03-10', 1, 0)", oldMessagesNotBusiness);

        int openMonthTrackedOnBusiness = await CreateAccountAsync(longAgo);
        await ExecAsync("""
            INSERT INTO dbo.WorkspaceMessageUsage (WorkspaceId, Day, Inbound, Outbound) VALUES (@p0, '2019-05-10', 1, 0);
            INSERT INTO dbo.BusinessTenantMonths (MonthStart, TenantId, TenantRefId) VALUES ('2019-05-01', @p0, NEWID());
            """, openMonthTrackedOnBusiness);

        int tenantOnBusinessNow = await CreateAccountAsync(longAgo);
        await ExecAsync("""
            INSERT INTO dbo.WorkspaceMessageUsage (WorkspaceId, Day, Inbound, Outbound) VALUES (@p0, '2019-03-10', 1, 0);
            INSERT INTO dbo.WorkspaceAccounts (WorkspaceId, TenantId, TenantRefId, PlanCode, PlanRevision)
            VALUES (@p0 - 1, @p0, NEWID(), N'Business', 1);
            """, tenantOnBusinessNow);

        int withRun = await CreateAccountAsync(longAgo);
        int definitionId = await InsertAsync<int>("""
            INSERT INTO dbo.WorkflowDefinitions (RefId, Version, WorkspaceId, Name, DefinitionJson, PublishedBy)
            OUTPUT INSERTED.Id VALUES (NEWID(), 1, @p0, 'prune-test', '{}', 1);
            """, withRun);
        await ExecAsync("""
            INSERT INTO dbo.Runs (RefId, WorkflowDefinitionId, WorkflowRefId, WorkflowVersion, TriggerNodeId, Status, StartedAt, CompletedAt, CreditBudget, WorkspaceId)
            VALUES (NEWID(), @p0, NEWID(), 1, NEWID(), N'Succeeded', '2026-08-01', '2026-08-01', 0, (SELECT WorkspaceId FROM dbo.WorkflowDefinitions WHERE Id = @p0));
            """, definitionId);

        int withReadings = await CreateAccountAsync(longAgo);
        var (policyId, _) = await CreatePolicyAsync(withReadings);
        int readingClient = await CreateClientAsync(withReadings, policyId, status: 2);
        await ExecAsync("""
            INSERT INTO dbo.ClientReadings (ClientId, Name, SentAt, ReceivedAt, NumberValue, IsLate)
            VALUES (@p0, 't', '2026-08-01', '2026-08-01', 1, 0);
            """, readingClient);

        int withHourly = await CreateAccountAsync(longAgo);
        var (hourlyPolicyId, _) = await CreatePolicyAsync(withHourly);
        int hourlyClient = await CreateClientAsync(withHourly, hourlyPolicyId, status: 2);
        await ExecAsync("""
            INSERT INTO dbo.ClientReadingsHourly (ClientId, Name, HourStart, ReadingCount, MinValue, MaxValue, SumValue)
            VALUES (@p0, 't', '2026-08-01', 1, 1, 1, 1);
            """, hourlyClient);

        // #965: a copy stays while the purge still has rows to remove, since it finds them by the copy.
        int notPurged = await CreateAccountAsync(longAgo);
        await CreatePolicyAsync(notPurged);

        Assert.Equal(1, await Provider().DeleteRetiredAccountsAsync(now, batchSize: 1));
        while (await Provider().DeleteRetiredAccountsAsync(now, batchSize: 1000) > 0)
        {
        }

        Assert.False(await AccountExistsAsync(unread));
        Assert.False(await AccountExistsAsync(justBeforeLastMonth));
        Assert.False(await AccountExistsAsync(creditsBeforeThat));
        Assert.False(await AccountExistsAsync(oldMessagesNotBusiness));
        Assert.True(await AccountExistsAsync(atLastMonth));
        Assert.True(await AccountExistsAsync(live));
        Assert.True(await AccountExistsAsync(creditsLastMonth));
        Assert.True(await AccountExistsAsync(messagesLastMonth));
        Assert.True(await AccountExistsAsync(openMonthTrackedOnBusiness));
        Assert.True(await AccountExistsAsync(tenantOnBusinessNow));
        Assert.True(await AccountExistsAsync(withRun));
        Assert.True(await AccountExistsAsync(withReadings));
        Assert.True(await AccountExistsAsync(withHourly));
        Assert.True(await AccountExistsAsync(notPurged));
    }

    /// <summary>
    /// A pruned copy no longer marks its workspace deleted, so a late report from one of its clients is
    /// kept out by the client's status instead: a retire revokes every client the workspace had.
    /// </summary>
    [SkippableFact]
    public async Task A_report_from_a_retired_workspace_is_dropped_after_its_copy_is_pruned()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var ws = await SeedWorkspaceAsync();
        await Provider().RetireAsync(ws.WorkspaceId);
        await ExecAsync("DELETE FROM dbo.WorkspaceAccounts WHERE WorkspaceId = @p0", ws.WorkspaceId);

        await ExecAsync("""
            DECLARE @r dbo.ClientReadingTableType;
            INSERT INTO @r (Name, NumberValue) VALUES (N't', 1);
            EXEC dbo.ClientReadings_InsertBatch @ClientId = @p0, @SentAt = '2026-11-01', @ReceivedAt = '2026-11-01', @IsLate = 0, @Readings = @r;
            """, ws.RegisteredClientId);

        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ClientReadings WHERE ClientId = @p0", ws.RegisteredClientId));
    }

    /// <summary>
    /// #965: once a deleted workspace's readings and runs are gone, the purge removes its clients (with
    /// what they reported about themselves), policies, templates, workflows and definitions, in that
    /// order, and leaves the neighbouring workspace alone. The account copy can then be pruned.
    /// </summary>
    [SkippableFact]
    public async Task A_deleted_workspace_is_purged_once_its_runs_are_gone()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var ws = await SeedWorkspaceAsync();
        await ExecAsync("""
            INSERT INTO dbo.WorkspaceAccounts (WorkspaceId, TenantId, TenantRefId, PlanCode, PlanRevision, RetiredAt)
            VALUES (@p0, @p0, NEWID(), N'Free', 1, '2026-01-01');
            INSERT INTO dbo.ClientTags (ClientId, Tag) VALUES (@p1, N'kitchen');
            INSERT INTO dbo.ClientStateVariables (ClientId, Name, DataType, ValueJson) VALUES (@p1, N'temp', N'number', N'21');
            INSERT INTO dbo.MessageTemplates (WorkspaceId, PolicyId, Name, MessageType, PayloadJson) VALUES (@p0, @p2, N't', N'cmd', N'{}');
            INSERT INTO dbo.SharedVariables (WorkflowRefId, VarName, VarType, ValueJson) VALUES (@p3, N'n', N'number', N'1');
            """, ws.WorkspaceId, ws.RegisteredClientId, ws.PolicyId, ws.WorkflowRefId);

        // Not retired yet: nothing goes.
        await ExecAsync("UPDATE dbo.WorkspaceAccounts SET RetiredAt = NULL WHERE WorkspaceId = @p0", ws.WorkspaceId);
        Assert.Equal(0, await Provider().PurgeAsync(ws.WorkspaceId, batchSize: 100));
        await ExecAsync("UPDATE dbo.WorkspaceAccounts SET RetiredAt = '2026-01-01' WHERE WorkspaceId = @p0", ws.WorkspaceId);

        await Provider().RetireAsync(ws.WorkspaceId);
        Assert.Contains(ws.WorkspaceId, await Provider().GetPurgeableAsync(batchSize: 100_000));

        // Two clients a call, then the policy and template once none is left. The runs keep the workflow.
        Assert.Equal(2, await Provider().PurgeAsync(ws.WorkspaceId, batchSize: 2));
        while (await Provider().PurgeAsync(ws.WorkspaceId, batchSize: 2) > 0)
        {
        }

        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Clients WHERE WorkspaceId = @p0", ws.WorkspaceId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ClientTags WHERE ClientId = @p0", ws.RegisteredClientId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RegistrationPolicies WHERE WorkspaceId = @p0", ws.WorkspaceId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.MessageTemplates WHERE WorkspaceId = @p0", ws.WorkspaceId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Workflows WHERE WorkspaceId = @p0", ws.WorkspaceId));
        Assert.DoesNotContain(ws.WorkspaceId, await Provider().GetPurgeableAsync(batchSize: 100_000));

        await ExecAsync("DELETE FROM dbo.Runs WHERE WorkflowDefinitionId = @p0", ws.DefinitionId);
        Assert.True(await Provider().PurgeAsync(ws.WorkspaceId, batchSize: 2) > 0);

        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Workflows WHERE WorkspaceId = @p0", ws.WorkspaceId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.WorkflowDefinitions WHERE WorkspaceId = @p0", ws.WorkspaceId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.SharedVariables WHERE WorkflowRefId = @p0", ws.WorkflowRefId));
        Assert.DoesNotContain(ws.WorkspaceId, await Provider().GetPurgeableAsync(batchSize: 100_000));
        Assert.Equal(0, await Provider().PurgeAsync(ws.WorkspaceId, batchSize: 2));

        // The neighbouring workspace is untouched.
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RegistrationPolicies WHERE Id = @p0", ws.OtherPolicyId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Clients WHERE Id = @p0", ws.OtherClientId));
    }

    /// <summary>A client with readings left waits for the readings sweep, and so do its policy and the account copy.</summary>
    [SkippableFact]
    public async Task A_client_with_readings_left_is_not_purged()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = await CreateAccountAsync(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var (policyId, _) = await CreatePolicyAsync(workspaceId);
        int clientId = await CreateClientAsync(workspaceId, policyId, status: 2);
        await ExecAsync("""
            INSERT INTO dbo.ClientReadings (ClientId, Name, SentAt, ReceivedAt, NumberValue, IsLate)
            VALUES (@p0, 't', '2026-08-01', '2026-08-01', 1, 0);
            """, clientId);

        Assert.DoesNotContain(workspaceId, await Provider().GetPurgeableAsync(batchSize: 100_000));
        Assert.Equal(0, await Provider().PurgeAsync(workspaceId, batchSize: 100));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Clients WHERE Id = @p0", clientId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RegistrationPolicies WHERE Id = @p0", policyId));

        await ExecAsync("DELETE FROM dbo.ClientReadings WHERE ClientId = @p0", clientId);

        Assert.Contains(workspaceId, await Provider().GetPurgeableAsync(batchSize: 100_000));
        Assert.Equal(2, await Provider().PurgeAsync(workspaceId, batchSize: 100));
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
            INSERT INTO dbo.Workflows (RefId, WorkspaceId, State, LiveDefinitionId, StateChangedBy)
            VALUES (@p0, @p1, 1, @p2, 1);
            """, workflowRefId, workspaceId, definitionId);

        await ExecAsync("""
            INSERT INTO dbo.TriggerRegistrations (WorkflowDefinitionId, WorkflowRefId, WorkflowVersion, TriggerNodeId, TriggerKind, TriggerKey)
            VALUES (@p0, @p1, 1, @p2, 'webhook', CONCAT('webhook:retire:', @p1));
            INSERT INTO dbo.ScheduledFires (TriggerNodeId, WorkflowDefinitionId, WorkflowRefId, CronOrInterval, NextFireAt)
            VALUES (@p2, @p0, @p1, '* * * * *', SYSUTCDATETIME());
            INSERT INTO dbo.PendingTriggerEvents (WorkflowRefId, TriggerNodeId, CorrelationKey, InboundEventJson)
            VALUES (@p1, @p2, 'k', '{}');
            INSERT INTO dbo.Runs (RefId, WorkflowDefinitionId, WorkflowRefId, WorkflowVersion, TriggerNodeId, Status, StartedAt, CompletedAt, CreditBudget, WorkspaceId)
            VALUES (NEWID(), @p0, @p1, 1, @p2, N'Succeeded', SYSUTCDATETIME(), SYSUTCDATETIME(), 0, (SELECT WorkspaceId FROM dbo.WorkflowDefinitions WHERE Id = @p0));
            """, definitionId, workflowRefId, triggerNodeId);

        long runningRunId = await InsertAsync<int>("""
            INSERT INTO dbo.Runs (RefId, WorkflowDefinitionId, WorkflowRefId, WorkflowVersion, TriggerNodeId, Status, StartedAt, CreditBudget, WorkspaceId)
            OUTPUT INSERTED.Id VALUES (NEWID(), @p0, @p1, 1, @p2, N'Running', SYSUTCDATETIME(), 0, (SELECT WorkspaceId FROM dbo.WorkflowDefinitions WHERE Id = @p0));
            """, definitionId, workflowRefId, triggerNodeId);

        return new SeededWorkspace(workspaceId, policyId, policyRefId, pending, registered, rejected,
            definitionId, workflowRefId, runningRunId, otherPolicyId, otherClient);
    }

    private async Task<int> CreateAccountAsync(DateTime? retiredAt)
    {
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        await ExecAsync("""
            INSERT INTO dbo.WorkspaceAccounts (WorkspaceId, TenantId, TenantRefId, PlanCode, PlanRevision, RetiredAt)
            VALUES (@p0, @p0, NEWID(), N'Free', 1, @p1);
            """, workspaceId, (object?)retiredAt ?? DBNull.Value);
        return workspaceId;
    }

    private async Task<bool> AccountExistsAsync(int workspaceId) =>
        await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.WorkspaceAccounts WHERE WorkspaceId = @p0", workspaceId) == 1;

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

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Management.Host.Providers;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// <c>dbo.RegistrationPolicy_Delete</c>: a policy goes only once no client is enrolled through it,
/// its pinned templates stay in the workspace, and its history outlives it.
/// </summary>
[Collection("SqlEdge")]
public sealed class RegistrationPolicyDeletionIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";

    [SkippableFact]
    public async Task A_policy_with_a_client_is_not_deleted()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = NewWorkspaceId();
        var (policyId, _) = await CreatePolicyAsync(workspaceId);
        await ExecAsync("""
            INSERT INTO dbo.Clients (WorkspaceId, PolicyId, Name, SecretHash, Status)
            VALUES (@p0, @p1, 'policy-delete-test', 0x00, 3);
            """, workspaceId, policyId);

        var outcome = await Provider().DeleteAsync(workspaceId, policyId);

        Assert.False(outcome.Deleted);
        Assert.Equal(1, outcome.ClientCount);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RegistrationPolicies WHERE Id = @p0", policyId));
    }

    [SkippableFact]
    public async Task An_empty_policy_is_deleted_its_templates_unpinned_and_its_history_kept()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = NewWorkspaceId();
        var (policyId, policyRefId) = await CreatePolicyAsync(workspaceId);
        int templateId = await ScalarAsync<int>("""
            INSERT INTO dbo.MessageTemplates (WorkspaceId, PolicyId, Name, MessageType, PayloadJson)
            OUTPUT INSERTED.Id VALUES (@p0, @p1, 'pinned', 'cmd/test', '{}');
            """, workspaceId, policyId);
        int eventId = await EventIdAsync();
        await ExecAsync("INSERT INTO dbo.EventLogs (EventId, WorkspaceId, PolicyId, PolicyRefId) VALUES (@p0, @p1, @p2, @p3)",
            eventId, workspaceId, policyId, policyRefId);

        var outcome = await Provider().DeleteAsync(workspaceId, policyId);

        Assert.True(outcome.Deleted);
        Assert.Equal(0, outcome.ClientCount);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RegistrationPolicies WHERE Id = @p0", policyId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.MessageTemplates WHERE Id = @p0 AND PolicyId IS NULL", templateId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.EventLogs WHERE PolicyRefId = @p0", policyRefId));

        // The deletion's own event is logged after the row is gone, and must not fail on it.
        await ExecAsync("INSERT INTO dbo.EventLogs (EventId, WorkspaceId, PolicyId, PolicyRefId) VALUES (@p0, @p1, @p2, @p3)",
            eventId, workspaceId, policyId, policyRefId);
        Assert.False((await Provider().DeleteAsync(workspaceId, policyId)).Deleted);
    }

    [SkippableFact]
    public async Task A_policy_in_another_workspace_is_left_alone()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = NewWorkspaceId();
        var (policyId, _) = await CreatePolicyAsync(workspaceId);

        var outcome = await Provider().DeleteAsync(workspaceId + 1, policyId);

        Assert.False(outcome.Deleted);
        Assert.Equal(0, outcome.ClientCount);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RegistrationPolicies WHERE Id = @p0", policyId));
    }

    // ---------------------------------------------------------------- helpers

    private static int NewWorkspaceId() => Random.Shared.Next(1_000_000, int.MaxValue - 1);

    private IRegistrationPolicyProvider Provider() =>
        new RegistrationPolicyProvider(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString })
            .Build());

    private async Task<(int Id, Guid RefId)> CreatePolicyAsync(int workspaceId)
    {
        Guid refId = Guid.NewGuid();
        int id = await ScalarAsync<int>("""
            INSERT INTO dbo.RegistrationPolicies (RefId, WorkspaceId, Pin, Name)
            OUTPUT INSERTED.Id VALUES (@p0, @p1, @p2, 'policy-delete-test');
            """, refId, workspaceId, Guid.NewGuid().ToString("N")[..20]);
        return (id, refId);
    }

    private Task<int> EventIdAsync() => ScalarAsync<int>("""
        IF NOT EXISTS (SELECT 1 FROM dbo.Events WHERE EventName = N'PolicyDeletionTestEvent')
            INSERT INTO dbo.Events (EventName, EventCriticality) VALUES (N'PolicyDeletionTestEvent', 0);
        SELECT Id FROM dbo.Events WHERE EventName = N'PolicyDeletionTestEvent';
        """);

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

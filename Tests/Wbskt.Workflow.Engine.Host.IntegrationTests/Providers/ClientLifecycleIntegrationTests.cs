using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// The procedures behind deleting a client, rotating its secret or its policy's PIN, and approving
/// against a policy's limit. Each test seeds its own workspace, so the shared database is safe.
/// </summary>
[Collection("SqlEdge")]
public sealed class ClientLifecycleIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";

    [SkippableFact]
    public async Task Deleting_a_client_removes_what_it_reported_and_keeps_its_history()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = NewWorkspaceId();
        int policyId = await CreatePolicyAsync(workspaceId);
        int clientId = await CreateClientAsync(workspaceId, policyId, status: 1);
        await ExecAsync("""
            INSERT INTO dbo.ClientCapabilities (ClientId, AgentName, AgentVersion, Platform, CapabilitiesJson) VALUES (@p0, 'a', '1', 'linux', '[]');
            INSERT INTO dbo.ClientStateVariables (ClientId, Name, DataType, ValueJson) VALUES (@p0, 'temp', 'number', '21');
            """, clientId);
        int eventId = await ScalarAsync<int>("""
            IF NOT EXISTS (SELECT 1 FROM dbo.Events WHERE EventName = N'ClientLifecycleTestEvent')
                INSERT INTO dbo.Events (EventName, EventCriticality) VALUES (N'ClientLifecycleTestEvent', 0);
            SELECT Id FROM dbo.Events WHERE EventName = N'ClientLifecycleTestEvent';
            """);
        await ExecAsync("INSERT INTO dbo.EventLogs (EventId, WorkspaceId, ClientId, ClientRefId) SELECT @p0, @p1, Id, RefId FROM dbo.Clients WHERE Id = @p2",
            eventId, workspaceId, clientId);

        Assert.False(await Clients().DeleteAsync(clientId, workspaceId + 1));
        Assert.True(await Clients().DeleteAsync(clientId, workspaceId));

        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Clients WHERE Id = @p0", clientId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ClientCapabilities WHERE ClientId = @p0", clientId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ClientStateVariables WHERE ClientId = @p0", clientId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.EventLogs WHERE ClientId = @p0 AND ClientRefId IS NOT NULL", clientId));

        // A message logged after the delete must not fail on a missing client.
        await ExecAsync("INSERT INTO dbo.EventLogs (EventId, WorkspaceId, ClientId) VALUES (@p0, @p1, @p2)", eventId, workspaceId, clientId);
        Assert.False(await Clients().DeleteAsync(clientId, workspaceId));
    }

    [SkippableFact]
    public async Task A_secret_changes_only_inside_its_workspace()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = NewWorkspaceId();
        int clientId = await CreateClientAsync(workspaceId, await CreatePolicyAsync(workspaceId), status: 1);
        byte[] hash = System.Security.Cryptography.RandomNumberGenerator.GetBytes(ClientCredential.SecretHashBytes);

        Assert.False(await Clients().UpdateSecretAsync(clientId, workspaceId + 1, hash));
        Assert.Equal(new byte[] { 0x00 }, await ScalarAsync<byte[]>("SELECT SecretHash FROM dbo.Clients WHERE Id = @p0", clientId));

        Assert.True(await Clients().UpdateSecretAsync(clientId, workspaceId, hash));
        Assert.Equal(hash, await ScalarAsync<byte[]>("SELECT SecretHash FROM dbo.Clients WHERE Id = @p0", clientId));
    }

    [SkippableFact]
    public async Task Rotating_a_pin_replaces_it_and_leaves_clients_alone()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = NewWorkspaceId();
        int policyId = await CreatePolicyAsync(workspaceId);
        int clientId = await CreateClientAsync(workspaceId, policyId, status: 1);
        string before = await ScalarAsync<string>("SELECT Pin FROM dbo.RegistrationPolicies WHERE Id = @p0", policyId);

        await Assert.ThrowsAsync<SqlException>(() => Policies().RotatePinAsync(workspaceId + 1, policyId));
        var rotated = await Policies().RotatePinAsync(workspaceId, policyId);

        Assert.NotEqual(before, rotated.Pin);
        Assert.Equal(rotated.Pin, await ScalarAsync<string>("SELECT Pin FROM dbo.RegistrationPolicies WHERE Id = @p0", policyId));
        Assert.Equal(1, await ScalarAsync<byte>("SELECT Status FROM dbo.Clients WHERE Id = @p0", clientId));
    }

    [SkippableFact]
    public async Task Approving_past_the_limit_is_refused_even_when_approvals_race()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = NewWorkspaceId();
        int policyId = await CreatePolicyAsync(workspaceId, maxClients: 2);
        await CreateClientAsync(workspaceId, policyId, status: 1);
        int[] pending = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => CreateClientAsync(workspaceId, policyId, status: 0)));
        Guid[] refs = await Task.WhenAll(pending.Select(RefIdOfAsync));

        var outcomes = await Task.WhenAll(refs.Select(async refId =>
            (await Clients().UpdateStatusesAsync(workspaceId, [refId], ClientStatus.Registered)).Single().Outcome));

        Assert.Equal(1, outcomes.Count(o => o == ClientStatusOutcome.Updated));
        Assert.All(outcomes.Where(o => o != ClientStatusOutcome.Updated), o => Assert.Equal(ClientStatusOutcome.PolicyLimitReached, o));
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Clients WHERE PolicyId = @p0 AND Status = 1", policyId));

        // Revoking is never limited, and an already-registered client can be re-saved as registered.
        // refs[0] may be the one that won the race, so its old status is whatever the table says now.
        var before = (ClientStatus)await ScalarAsync<byte>("SELECT Status FROM dbo.Clients WHERE RefId = @p0", refs[0]);
        var revoked = (await Clients().UpdateStatusesAsync(workspaceId, [refs[0]], ClientStatus.Revoked)).Single();
        Assert.Equal(ClientStatusOutcome.Updated, revoked.Outcome);
        Assert.Equal(before, revoked.OldStatus);
        Guid registered = await ScalarAsync<Guid>("SELECT TOP 1 RefId FROM dbo.Clients WHERE PolicyId = @p0 AND Status = 1", policyId);
        Assert.Equal(ClientStatusOutcome.AlreadyAtStatus,
            (await Clients().UpdateStatusesAsync(workspaceId, [registered], ClientStatus.Registered)).Single().Outcome);
    }

    [SkippableFact]
    public async Task A_batch_fills_a_policys_last_places_in_the_order_given_and_reports_every_client()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = NewWorkspaceId();
        int policyId = await CreatePolicyAsync(workspaceId, maxClients: 3);
        int unlimitedPolicyId = await CreatePolicyAsync(workspaceId);
        await CreateClientAsync(workspaceId, policyId, status: 1);
        Guid[] limited = await Task.WhenAll(Enumerable.Range(0, 3).Select(async _ => await RefIdOfAsync(await CreateClientAsync(workspaceId, policyId, status: 0))));
        Guid unlimited = await RefIdOfAsync(await CreateClientAsync(workspaceId, unlimitedPolicyId, status: 0));
        Guid foreign = await RefIdOfAsync(await CreateClientAsync(NewWorkspaceId(), await CreatePolicyAsync(workspaceId + 1), status: 0));
        Guid alreadyRegistered = await RefIdOfAsync(await CreateClientAsync(workspaceId, unlimitedPolicyId, status: 1));
        Guid missing = Guid.NewGuid();

        var changes = await Clients().UpdateStatusesAsync(workspaceId,
            [limited[2], unlimited, foreign, limited[0], missing, alreadyRegistered, limited[1]], ClientStatus.Registered);

        Assert.Equal(
            new[]
            {
                (limited[2], ClientStatusOutcome.Updated),
                (unlimited, ClientStatusOutcome.Updated),
                (foreign, ClientStatusOutcome.OtherWorkspace),
                (limited[0], ClientStatusOutcome.Updated),
                (missing, ClientStatusOutcome.NotFound),
                (alreadyRegistered, ClientStatusOutcome.AlreadyAtStatus),
                (limited[1], ClientStatusOutcome.PolicyLimitReached)
            },
            changes.Select(c => (c.RefId, c.Outcome)));
        Assert.Equal(3, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Clients WHERE PolicyId = @p0 AND Status = 1", policyId));
        Assert.Equal(0, await ScalarAsync<byte>("SELECT Status FROM dbo.Clients WHERE RefId = @p0", foreign));
        var updated = changes[0];
        Assert.Equal(policyId, updated.PolicyId);
        Assert.Equal(await ScalarAsync<Guid>("SELECT RefId FROM dbo.RegistrationPolicies WHERE Id = @p0", policyId), updated.PolicyRefId);
        Assert.Equal(ClientStatus.Pending, updated.OldStatus);
    }

    // ---------------------------------------------------------------- helpers

    private static int NewWorkspaceId() => Random.Shared.Next(1_000_000, int.MaxValue - 1);

    private IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString })
        .Build();

    private IClientProvider Clients() => new ClientProvider(Configuration());

    private IRegistrationPolicyProvider Policies() => new RegistrationPolicyProvider(Configuration());

    private Task<int> CreatePolicyAsync(int workspaceId, int? maxClients = null) =>
        ScalarAsync<int>("""
            INSERT INTO dbo.RegistrationPolicies (RefId, WorkspaceId, Pin, Name, MaxClients)
            OUTPUT INSERTED.Id VALUES (NEWID(), @p0, @p1, 'lifecycle-test', @p2);
            """, workspaceId, Guid.NewGuid().ToString("N")[..20], (object?)maxClients ?? DBNull.Value);

    private Task<int> CreateClientAsync(int workspaceId, int policyId, byte status) =>
        ScalarAsync<int>("""
            INSERT INTO dbo.Clients (WorkspaceId, PolicyId, Name, SecretHash, Status)
            OUTPUT INSERTED.Id VALUES (@p0, @p1, 'lifecycle-test', 0x00, @p2);
            """, workspaceId, policyId, status);

    private Task<Guid> RefIdOfAsync(int clientId) => ScalarAsync<Guid>("SELECT RefId FROM dbo.Clients WHERE Id = @p0", clientId);

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

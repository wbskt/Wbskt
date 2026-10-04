using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;
using Wbskt.Workflow.Engine.Host.Providers;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// The procedures behind presence triggers: finding the triggers a connect or disconnect concerns
/// (only in the client's own workspace), parking a change once however often it is delivered, and
/// leasing due checks together with the client's current presence.
/// </summary>
[Collection("SqlEdge")]
public sealed class ClientPresenceCheckIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";

    [SkippableFact]
    public async Task Trigger_keys_are_scoped_to_the_clients_workspace()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        (_, Guid clientRef) = await CreateClientAsync(workspaceId);
        string ownKey = await RegisterAsync(workspaceId, clientRef, ClientPresenceState.Offline, 60);
        // Another workspace naming this client's id must not be able to watch it.
        await RegisterAsync(workspaceId - 1, clientRef, ClientPresenceState.Offline, 60);
        await RegisterAsync(workspaceId, clientRef, ClientPresenceState.Online, 0);
        var provider = new ClientPresenceCheckProvider(ProviderFactory.BuildConfiguration(fixture.ConnectionString));

        IReadOnlyCollection<string> keys = await provider.GetTriggerKeysAsync(ClientPresenceTriggerKey.Prefix(clientRef, ClientPresenceState.Offline), workspaceId, CancellationToken.None);

        Assert.Equal([ownKey], keys);
    }

    [SkippableFact]
    public async Task A_redelivered_change_is_parked_once_and_leased_with_the_clients_presence()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        (int clientId, Guid clientRef) = await CreateClientAsync(workspaceId);
        string key = ClientPresenceTriggerKey.Build(clientRef, ClientPresenceState.Offline, 60, Guid.NewGuid(), Guid.NewGuid());
        DateTime changedAt = DateTime.UtcNow.AddMinutes(-5);
        await ProcAsync("dbo.Client_UpdatePresence", ("@Id", clientId), ("@IsConnected", false), ("@LastActivityAt", changedAt));
        var provider = new ClientPresenceCheckProvider(ProviderFactory.BuildConfiguration(fixture.ConnectionString));

        await provider.InsertAsync(key, clientRef, "offline", changedAt, changedAt.AddSeconds(60), CancellationToken.None);
        await provider.InsertAsync(key, clientRef, "offline", changedAt, changedAt.AddSeconds(60), CancellationToken.None);

        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ClientPresenceChecks WHERE TriggerKey = @p0", key));

        ClientPresenceCheckRow check = (await provider.LeaseDueAsync(15, 1000, CancellationToken.None)).Single(c => c.TriggerKey == key);
        Assert.Equal(clientId, check.ClientId);
        Assert.Equal(workspaceId, check.ClientWorkspaceId);
        Assert.False(check.ClientIsConnected);
        Assert.Null(check.ClientConnectedAt);
        // Both sides went through the same parameter and column types, so they agree to the millisecond.
        Assert.Equal(check.ChangedAt, check.ClientLastActivityAt);

        // Leased: a second tick does not see it until the lease runs out.
        Assert.DoesNotContain(await provider.LeaseDueAsync(15, 1000, CancellationToken.None), c => c.TriggerKey == key);

        await provider.DeleteByIdAsync(check.Id, CancellationToken.None);
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ClientPresenceChecks WHERE TriggerKey = @p0", key));
    }

    [SkippableFact]
    public async Task A_disconnect_applied_before_its_connect_leaves_the_client_offline()
    {
        // A client that drops straight after connecting: the two events can be consumed out of order.
        Skip.IfNot(fixture.IsAvailable, Skipped);
        (int clientId, _) = await CreateClientAsync(Random.Shared.Next(1_000_000, int.MaxValue));
        // Whole seconds, so the value survives the DATETIME2(3) column unchanged.
        DateTime now = DateTime.UtcNow;
        DateTime connectedAt = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc).AddMinutes(-1);
        DateTime disconnectedAt = connectedAt.AddSeconds(1);

        await PresenceAsync(clientId, false, disconnectedAt, "host-a");
        await PresenceAsync(clientId, true, connectedAt, "host-a");

        Assert.False(await ScalarAsync<bool>("SELECT IsConnected FROM dbo.Clients WHERE Id = @p0", clientId));
        Assert.Equal(disconnectedAt, await ScalarAsync<DateTime>("SELECT LastActivityAt FROM dbo.Clients WHERE Id = @p0", clientId));
    }

    [SkippableFact]
    public async Task Presence_changes_in_order_still_apply()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        (int clientId, _) = await CreateClientAsync(Random.Shared.Next(1_000_000, int.MaxValue));
        DateTime connectedAt = DateTime.UtcNow.AddMinutes(-1);

        await PresenceAsync(clientId, true, connectedAt, "host-a");
        Assert.True(await ScalarAsync<bool>("SELECT IsConnected FROM dbo.Clients WHERE Id = @p0", clientId));

        await PresenceAsync(clientId, false, connectedAt.AddSeconds(1), "host-a");
        Assert.False(await ScalarAsync<bool>("SELECT IsConnected FROM dbo.Clients WHERE Id = @p0", clientId));

        await PresenceAsync(clientId, true, connectedAt.AddSeconds(2), "host-a");
        Assert.True(await ScalarAsync<bool>("SELECT IsConnected FROM dbo.Clients WHERE Id = @p0", clientId));
    }

    [SkippableFact]
    public async Task A_late_disconnect_from_a_superseded_host_leaves_the_client_online()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        (int clientId, _) = await CreateClientAsync(Random.Shared.Next(1_000_000, int.MaxValue));
        DateTime connectedAt = DateTime.UtcNow.AddMinutes(-1);

        await PresenceAsync(clientId, true, connectedAt, "host-b");
        await PresenceAsync(clientId, false, connectedAt.AddSeconds(1), "host-a");

        Assert.True(await ScalarAsync<bool>("SELECT IsConnected FROM dbo.Clients WHERE Id = @p0", clientId));
    }

    [SkippableFact]
    public async Task A_check_for_a_deleted_client_leases_with_no_client()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        Guid clientRef = Guid.NewGuid();
        string key = ClientPresenceTriggerKey.Build(clientRef, ClientPresenceState.Offline, 0, Guid.NewGuid(), Guid.NewGuid());
        DateTime changedAt = DateTime.UtcNow.AddMinutes(-1);
        var provider = new ClientPresenceCheckProvider(ProviderFactory.BuildConfiguration(fixture.ConnectionString));
        await provider.InsertAsync(key, clientRef, "offline", changedAt, changedAt, CancellationToken.None);

        ClientPresenceCheckRow check = (await provider.LeaseDueAsync(15, 1000, CancellationToken.None)).Single(c => c.TriggerKey == key);

        Assert.Null(check.ClientId);
        Assert.Null(check.ClientIsConnected);
        await provider.DeleteByIdAsync(check.Id, CancellationToken.None);
    }

    private async Task<(int ClientId, Guid ClientRef)> CreateClientAsync(int workspaceId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = Command("""
            DECLARE @PolicyId INT;
            INSERT INTO dbo.RegistrationPolicies (WorkspaceId, Pin, Name) VALUES (@p0, LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), '-', ''), 20), N'presence');
            SET @PolicyId = SCOPE_IDENTITY();
            INSERT INTO dbo.Clients (WorkspaceId, PolicyId, Name, SecretHash) OUTPUT INSERTED.Id, INSERTED.RefId
            VALUES (@p0, @PolicyId, N'presence', 0x00);
            """, conn, [workspaceId]);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt32(0), reader.GetGuid(1));
    }

    private async Task<string> RegisterAsync(int workspaceId, Guid clientRef, ClientPresenceState state, int forSeconds)
    {
        var workflowRefId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        await ProcAsync("dbo.WorkflowDefinition_Publish",
            ("@RefId", workflowRefId), ("@WorkspaceId", workspaceId), ("@Name", "wf"), ("@Description", ""),
            ("@IsEnabled", true), ("@DefinitionJson", "{}"), ("@PublishedBy", 1));
        int definitionId = await ScalarAsync<int>("SELECT Id FROM dbo.WorkflowDefinitions WHERE RefId = @p0", workflowRefId);
        string key = ClientPresenceTriggerKey.Build(clientRef, state, forSeconds, workflowRefId, nodeId);
        await ProcAsync("dbo.TriggerRegistration_Insert",
            ("@WorkflowDefinitionId", definitionId), ("@WorkflowRefId", workflowRefId), ("@WorkflowVersion", 1),
            ("@TriggerNodeId", nodeId), ("@TriggerKind", "presence"), ("@TriggerKey", key),
            ("@CorrelationExpression", DBNull.Value), ("@ConcurrencyPolicy", "Queue"), ("@FilterExpression", DBNull.Value));
        return key;
    }

    private async Task PresenceAsync(int clientId, bool isConnected, DateTime at, string hostId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.Client_UpdatePresence", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Id", clientId);
        cmd.Parameters.AddWithValue("@IsConnected", isConnected);
        cmd.Parameters.Add("@LastActivityAt", SqlDbType.DateTime2).Value = at;
        cmd.Parameters.AddWithValue("@HostId", hostId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task ProcAsync(string procedure, params (string Name, object Value)[] parameters)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand(procedure, conn) { CommandType = CommandType.StoredProcedure };
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.NextResultAsync())
        {
        }
    }

    private async Task<T> ScalarAsync<T>(string sql, params object[] args)
    {
        await using var conn = await OpenAsync();
        await using var cmd = Command(sql, conn, args);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private static SqlCommand Command(string sql, SqlConnection conn, object[] args)
    {
        var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < args.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        }

        return cmd;
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }
}

using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Management.Host.Providers;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// Presence as a lease on socket-host heartbeats: Client_ExpirePresence clears the clients held by a
/// host that has gone silent, and only those. Client_ResetPresenceBy_HostId covers the case the lease
/// cannot: a host restarted under the same id, renewing the lease over connections that died with it.
/// </summary>
[Collection("SqlEdge")]
public sealed class SocketHostPresenceIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    [SkippableFact]
    public async Task Clients_of_a_silent_host_are_expired_once_and_others_are_left_alone()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var configuration = ProviderFactory.BuildConfiguration(fixture.ConnectionString);
        var clients = new ClientProvider(configuration);
        var hosts = new SocketHostProvider(configuration);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        string deadHost = $"dead-{Guid.NewGuid():N}";
        string liveHost = $"live-{Guid.NewGuid():N}";
        string newHost = $"new-{Guid.NewGuid():N}";

        (int deadClient, Guid deadRef) = await CreateClientAsync(workspaceId);
        (int liveClient, _) = await CreateClientAsync(workspaceId);
        (int newClient, _) = await CreateClientAsync(workspaceId);
        await PresenceAsync(deadClient, DateTime.UtcNow.AddMinutes(-10), deadHost);
        await PresenceAsync(liveClient, DateTime.UtcNow.AddMinutes(-10), liveHost);
        // Connected moments ago on a host whose first heartbeat has not been recorded yet.
        await PresenceAsync(newClient, DateTime.UtcNow.AddSeconds(-5), newHost);
        await hosts.RecordHeartbeatAsync(liveHost);
        await hosts.RecordHeartbeatAsync(liveHost);

        var expired = await clients.ExpirePresenceAsync(StaleAfter);

        var mine = expired.Where(e => e.WorkspaceId == workspaceId).ToList();
        var only = Assert.Single(mine);
        Assert.Equal(deadClient, only.ClientId);
        Assert.Equal(deadRef, only.ClientRefId);
        Assert.Equal(deadHost, only.HostId);
        Assert.False(await IsConnectedAsync(deadClient));
        Assert.True(await IsConnectedAsync(liveClient));
        Assert.True(await IsConnectedAsync(newClient));

        // Claimed once: a second sweep does not report it again.
        Assert.DoesNotContain(await clients.ExpirePresenceAsync(StaleAfter), e => e.ClientId == deadClient);
    }

    [SkippableFact]
    public async Task A_restarted_host_clears_only_the_clients_it_held_even_while_its_lease_is_fresh()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var configuration = ProviderFactory.BuildConfiguration(fixture.ConnectionString);
        var clients = new ClientProvider(configuration);
        var hosts = new SocketHostProvider(configuration);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        string restartedHost = $"restarted-{Guid.NewGuid():N}";
        string otherHost = $"other-{Guid.NewGuid():N}";

        (int heldClient, Guid heldRef) = await CreateClientAsync(workspaceId);
        (int otherClient, _) = await CreateClientAsync(workspaceId);
        await PresenceAsync(heldClient, DateTime.UtcNow.AddMinutes(-10), restartedHost);
        await PresenceAsync(otherClient, DateTime.UtcNow.AddMinutes(-10), otherHost);
        // The restarted process heartbeats at once, so the lease sweep would never expire its clients.
        await hosts.RecordHeartbeatAsync(restartedHost);
        Assert.DoesNotContain(await clients.ExpirePresenceAsync(StaleAfter), e => e.ClientId == heldClient);

        var cleared = await clients.ResetPresenceByHostAsync(restartedHost);

        var only = Assert.Single(cleared);
        Assert.Equal(heldClient, only.ClientId);
        Assert.Equal(heldRef, only.ClientRefId);
        Assert.Equal(restartedHost, only.HostId);
        Assert.False(await IsConnectedAsync(heldClient));
        Assert.True(await IsConnectedAsync(otherClient));
        Assert.Empty(await clients.ResetPresenceByHostAsync(restartedHost));
    }

    private async Task<(int ClientId, Guid ClientRef)> CreateClientAsync(int workspaceId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("""
            DECLARE @PolicyId INT;
            INSERT INTO dbo.RegistrationPolicies (WorkspaceId, Pin, Name) VALUES (@WorkspaceId, LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), '-', ''), 20), N'lease');
            SET @PolicyId = SCOPE_IDENTITY();
            INSERT INTO dbo.Clients (WorkspaceId, PolicyId, Name, SecretHash) OUTPUT INSERTED.Id, INSERTED.RefId
            VALUES (@WorkspaceId, @PolicyId, N'lease', 0x00);
            """, conn);
        cmd.Parameters.AddWithValue("@WorkspaceId", workspaceId);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt32(0), reader.GetGuid(1));
    }

    private async Task PresenceAsync(int clientId, DateTime connectedAt, string hostId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("dbo.Client_UpdatePresence", conn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Id", clientId);
        cmd.Parameters.AddWithValue("@IsConnected", true);
        cmd.Parameters.Add("@LastActivityAt", SqlDbType.DateTime2).Value = connectedAt;
        cmd.Parameters.AddWithValue("@HostId", hostId);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<bool> IsConnectedAsync(int clientId)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("SELECT IsConnected FROM dbo.Clients WHERE Id = @Id", conn);
        cmd.Parameters.AddWithValue("@Id", clientId);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        return conn;
    }
}

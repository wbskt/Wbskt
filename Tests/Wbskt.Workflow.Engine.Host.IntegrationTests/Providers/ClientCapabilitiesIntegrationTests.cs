using Microsoft.Data.SqlClient;
using Wbskt.Management.Host.Providers;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>ClientCapabilities_Upsert keeps the newest report by when the gateway received it, whatever order they arrive in.</summary>
[Collection("SqlEdge")]
public sealed class ClientCapabilitiesIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";

    [SkippableFact]
    public async Task An_older_report_arriving_late_does_not_overwrite_a_newer_one()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var clients = new ClientProvider(ProviderFactory.BuildConfiguration(fixture.ConnectionString));
        int clientId = await CreateClientAsync(Random.Shared.Next(1_000_000, int.MaxValue));
        var older = DateTime.UtcNow.AddSeconds(-30);
        var newer = DateTime.UtcNow;

        Assert.True(await clients.UpsertCapabilitiesAsync(clientId, "wbskt-esp32", "0.3.0", "esp32", "[]", null, "1.1.0", newer));
        Assert.False(await clients.UpsertCapabilitiesAsync(clientId, "wbskt-esp32", "0.3.0", "esp32", "[]", """["ota"]""", "1.0.0", older));
        Assert.True(await clients.UpsertCapabilitiesAsync(clientId, "wbskt-esp32", "0.3.0", "esp32", "[]", null, "1.1.0", newer));

        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT SystemJson, FirmwareVersion FROM dbo.ClientCapabilities WHERE ClientId = @Id", conn);
        cmd.Parameters.AddWithValue("@Id", clientId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.IsDBNull(0));
        Assert.Equal("1.1.0", reader.GetString(1));
    }

    private async Task<int> CreateClientAsync(int workspaceId)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("""
            DECLARE @PolicyId INT;
            INSERT INTO dbo.RegistrationPolicies (WorkspaceId, Pin, Name) VALUES (@WorkspaceId, LEFT(REPLACE(CONVERT(NVARCHAR(36), NEWID()), '-', ''), 20), N'caps');
            SET @PolicyId = SCOPE_IDENTITY();
            INSERT INTO dbo.Clients (WorkspaceId, PolicyId, Name, SecretHash) OUTPUT INSERTED.Id
            VALUES (@WorkspaceId, @PolicyId, N'caps', 0x00);
            """, conn);
        cmd.Parameters.AddWithValue("@WorkspaceId", workspaceId);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}

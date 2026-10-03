using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Management.Host.Services;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// <c>dbo.Client_Create</c> enforces a policy's <c>MaxClients</c> under a lock on the policy row. The
/// service's earlier count is check-then-insert, so without this concurrent registrations against a
/// policy one short of its limit all got in.
/// </summary>
[Collection("SqlEdge")]
public sealed class ClientRegistrationLimitIntegrationTests(SqlEdgeFixture fixture)
{
    private const int PolicyLimitReachedError = 50020;

    [SkippableFact]
    public async Task Registration_beyond_MaxClients_is_refused()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Server is not reachable.");

        int policyId = await CreatePolicyAsync(maxClients: 2);

        await CreateClientAsync(policyId);
        await CreateClientAsync(policyId);

        var ex = await Assert.ThrowsAsync<SqlException>(() => CreateClientAsync(policyId));
        Assert.Equal(PolicyLimitReachedError, ex.Number);
        Assert.Equal(2, await CountClientsAsync(policyId));
    }

    [SkippableFact]
    public async Task Concurrent_registrations_cannot_overshoot_MaxClients()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Server is not reachable.");

        int policyId = await CreatePolicyAsync(maxClients: 3);

        Task[] attempts = Enumerable.Range(0, 12).Select(_ => Task.Run(() => CreateClientAsync(policyId))).ToArray();
        try
        {
            await Task.WhenAll(attempts);
        }
        catch (SqlException)
        {
            // Expected for the attempts past the limit; each is checked below.
        }

        Assert.All(
            attempts.Where(t => t.IsFaulted),
            t => Assert.Equal(PolicyLimitReachedError, Assert.IsType<SqlException>(t.Exception!.InnerException).Number));
        Assert.Equal(3, attempts.Count(t => t.IsCompletedSuccessfully));
        Assert.Equal(3, await CountClientsAsync(policyId));
    }

    [SkippableFact]
    public async Task Policy_without_a_limit_accepts_any_number()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Server is not reachable.");

        int policyId = await CreatePolicyAsync(maxClients: null);

        for (int i = 0; i < 5; i++)
        {
            await CreateClientAsync(policyId);
        }

        Assert.Equal(5, await CountClientsAsync(policyId));
    }

    private async Task<int> CreatePolicyAsync(int? maxClients)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("dbo.RegistrationPolicy_Create", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@WorkspaceId", 1);
        command.Parameters.AddWithValue("@Name", $"limit-test-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("@MaxClients", (object?)maxClients ?? DBNull.Value);
        command.Parameters.AddWithValue("@AutoApproval", true);
        command.Parameters.AddWithValue("@Pin", RegistrationPins.Generate());
        command.Parameters.Add("@Id", SqlDbType.Int).Direction = ParameterDirection.Output;
        command.Parameters.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        await command.ExecuteNonQueryAsync();

        return (int)command.Parameters["@Id"].Value;
    }

    private async Task CreateClientAsync(int policyId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("dbo.Client_Create", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@WorkspaceId", 1);
        command.Parameters.AddWithValue("@PolicyId", policyId);
        command.Parameters.AddWithValue("@Name", "device");
        command.Parameters.Add("@SecretHash", SqlDbType.VarBinary, 32).Value = ClientSecrets.Hash(ClientSecrets.Generate());
        command.Parameters.AddWithValue("@Status", (byte)1);
        command.Parameters.Add("@Id", SqlDbType.Int).Direction = ParameterDirection.Output;
        command.Parameters.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> CountClientsAsync(int policyId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("SELECT COUNT(*) FROM dbo.Clients WHERE PolicyId = @PolicyId;", connection);
        command.Parameters.AddWithValue("@PolicyId", policyId);

        return (int)(await command.ExecuteScalarAsync())!;
    }
}

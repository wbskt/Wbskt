using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Management.Host.Services;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// <c>dbo.Client_Create</c> enforces a policy's <c>MaxClients</c> under a lock on the policy row. The
/// service's earlier count is check-then-insert, so without this concurrent registrations against a
/// policy one short of its limit all got in. It also enforces the plan's limit on registered clients,
/// for a client that is created registered; with no tenant given, it counts the workspace alone.
/// </summary>
[Collection("SqlEdge")]
public sealed class ClientRegistrationLimitIntegrationTests(SqlEdgeFixture fixture)
{
    private const int PolicyLimitReachedError = 50020;
    private const int PlanLimitReachedError = 50025;

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

    [SkippableFact]
    public async Task The_plan_limit_spans_the_workspaces_policies_and_counts_registered_clients_only()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Server is not reachable.");

        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        int first = await CreatePolicyAsync(maxClients: null, workspaceId);
        int second = await CreatePolicyAsync(maxClients: null, workspaceId);

        await CreateClientAsync(first, workspaceId, clientLimit: 3);
        int revoked = await CreateClientAsync(second, workspaceId, clientLimit: 3, status: 2);
        await CreateClientAsync(second, workspaceId, clientLimit: 3, status: 0);
        await CreateClientAsync(first, workspaceId, clientLimit: 3);
        await CreateClientAsync(second, workspaceId, clientLimit: 3);

        var ex = await Assert.ThrowsAsync<SqlException>(() => CreateClientAsync(second, workspaceId, clientLimit: 3));
        Assert.Equal(PlanLimitReachedError, ex.Number);
        Assert.True(revoked > 0);

        // Another workspace is unaffected.
        int elsewhere = await CreatePolicyAsync(maxClients: null, workspaceId - 1);
        await CreateClientAsync(elsewhere, workspaceId - 1, clientLimit: 3);
    }

    [SkippableFact]
    public async Task Pending_registrations_are_not_refused_by_a_full_plan_and_do_not_fill_it()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Server is not reachable.");

        // A leaked PIN for a policy that needs approval must not lock out every other policy.
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        int approvalRequired = await CreatePolicyAsync(maxClients: null, workspaceId);
        int autoApproved = await CreatePolicyAsync(maxClients: null, workspaceId);

        for (int i = 0; i < 5; i++)
        {
            await CreateClientAsync(approvalRequired, workspaceId, clientLimit: 2, status: 0);
        }

        await CreateClientAsync(autoApproved, workspaceId, clientLimit: 2);
        await CreateClientAsync(autoApproved, workspaceId, clientLimit: 2);

        // Full of registered clients now: another registered one is refused, a pending one is not.
        var ex = await Assert.ThrowsAsync<SqlException>(() => CreateClientAsync(autoApproved, workspaceId, clientLimit: 2));
        Assert.Equal(PlanLimitReachedError, ex.Number);
        await CreateClientAsync(approvalRequired, workspaceId, clientLimit: 2, status: 0);
    }

    [SkippableFact]
    public async Task Concurrent_registrations_cannot_overshoot_the_plan_limit()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Server is not reachable.");

        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        int[] policies = [await CreatePolicyAsync(maxClients: null, workspaceId), await CreatePolicyAsync(maxClients: null, workspaceId)];

        Task[] attempts = Enumerable.Range(0, 12).Select(i => Task.Run(() => CreateClientAsync(policies[i % 2], workspaceId, clientLimit: 4))).ToArray();
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
            t => Assert.Equal(PlanLimitReachedError, Assert.IsType<SqlException>(t.Exception!.InnerException).Number));
        Assert.Equal(4, attempts.Count(t => t.IsCompletedSuccessfully));
    }

    private async Task<int> CreatePolicyAsync(int? maxClients, int workspaceId = 1)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("dbo.RegistrationPolicy_Create", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@WorkspaceId", workspaceId);
        command.Parameters.AddWithValue("@Name", $"limit-test-{Guid.NewGuid():N}");
        command.Parameters.AddWithValue("@MaxClients", (object?)maxClients ?? DBNull.Value);
        command.Parameters.AddWithValue("@AutoApproval", true);
        command.Parameters.AddWithValue("@Pin", RegistrationPins.Generate());
        command.Parameters.Add("@Id", SqlDbType.Int).Direction = ParameterDirection.Output;
        command.Parameters.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        await command.ExecuteNonQueryAsync();

        return (int)command.Parameters["@Id"].Value;
    }

    private async Task<int> CreateClientAsync(int policyId, int workspaceId = 1, int? clientLimit = null, byte status = 1)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("dbo.Client_Create", connection) { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@WorkspaceId", workspaceId);
        command.Parameters.AddWithValue("@PolicyId", policyId);
        command.Parameters.AddWithValue("@Name", "client");
        command.Parameters.Add("@SecretHash", SqlDbType.VarBinary, 32).Value = ClientSecrets.Hash(ClientSecrets.Generate());
        command.Parameters.AddWithValue("@Status", status);
        command.Parameters.AddWithValue("@ClientLimit", (object?)clientLimit ?? DBNull.Value);
        command.Parameters.Add("@Id", SqlDbType.Int).Direction = ParameterDirection.Output;
        command.Parameters.Add("@RefId", SqlDbType.UniqueIdentifier).Direction = ParameterDirection.Output;
        await command.ExecuteNonQueryAsync();

        return (int)command.Parameters["@Id"].Value;
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

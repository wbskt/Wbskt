using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Models;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// <c>dbo.RegistrationPolicy_Update</c> is a partial edit: a field left out keeps its stored value,
/// so two members changing different fields don't undo each other.
/// </summary>
[Collection("SqlEdge")]
public sealed class RegistrationPolicyUpdateIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";

    [SkippableFact]
    public async Task Changing_one_field_keeps_the_others()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = NewWorkspaceId();
        int policyId = await CreatePolicyAsync(workspaceId);

        await Provider().UpdateAsync(workspaceId, policyId, new UpdateRegistrationPolicyRequest(IsEnabled: false));

        Assert.Equal(("policy-update-test", 10, false, false), await ReadAsync(policyId));
    }

    [SkippableFact]
    public async Task A_new_limit_is_written_and_removing_it_makes_the_policy_unlimited()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = NewWorkspaceId();
        int policyId = await CreatePolicyAsync(workspaceId);

        await Provider().UpdateAsync(workspaceId, policyId, new UpdateRegistrationPolicyRequest(Name: "porch", MaxClients: 50));
        Assert.Equal(("porch", 50, false, true), await ReadAsync(policyId));

        await Provider().UpdateAsync(workspaceId, policyId, new UpdateRegistrationPolicyRequest(RemoveMaxClients: true));
        Assert.Equal(("porch", (int?)null, false, true), await ReadAsync(policyId));
    }

    // ---------------------------------------------------------------- helpers

    private static int NewWorkspaceId() => Random.Shared.Next(1_000_000, int.MaxValue - 1);

    private IRegistrationPolicyProvider Provider() =>
        new RegistrationPolicyProvider(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString })
            .Build());

    private async Task<int> CreatePolicyAsync(int workspaceId)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("""
            INSERT INTO dbo.RegistrationPolicies (WorkspaceId, Pin, Name, MaxClients, AutoApproval, IsEnabled)
            OUTPUT INSERTED.Id VALUES (@WorkspaceId, @Pin, 'policy-update-test', 10, 0, 1);
            """, conn);
        cmd.Parameters.AddWithValue("@WorkspaceId", workspaceId);
        cmd.Parameters.AddWithValue("@Pin", Guid.NewGuid().ToString("N")[..20]);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task<(string Name, int? MaxClients, bool AutoApproval, bool IsEnabled)> ReadAsync(int policyId)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT Name, MaxClients, AutoApproval, IsEnabled FROM dbo.RegistrationPolicies WHERE Id = @Id", conn);
        cmd.Parameters.AddWithValue("@Id", policyId);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt32(1), reader.GetBoolean(2), reader.GetBoolean(3));
    }
}

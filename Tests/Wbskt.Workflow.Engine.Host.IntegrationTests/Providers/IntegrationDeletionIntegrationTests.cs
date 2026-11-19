using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Wbskt.Management.Host.Providers;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// <c>dbo.Integration_Delete</c> for an integration only a version that is no longer live uses: no
/// published step blocks it, but runs still in flight on that version would fail at the step.
/// </summary>
[Collection("SqlEdge")]
public sealed class IntegrationDeletionIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";

    [SkippableFact]
    public async Task A_run_in_flight_on_a_version_using_it_refuses_the_delete_until_allowed()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        var (integrationId, definitionId, workflowRef, stepId) = await SeedAsync(workspaceId);
        await AddRunAsync(definitionId, workflowRef, "Running");
        await AddRunAsync(definitionId, workflowRef, "Failing");
        await AddRunAsync(definitionId, workflowRef, "Succeeded");

        var refused = await Provider().DeleteAsync(workspaceId, integrationId, allowActiveRuns: false, CancellationToken.None);

        var use = Assert.Single(refused);
        Assert.Equal((workflowRef, stepId, false, 2), (use.WorkflowRefId, use.NodeId, use.IsLive, use.ActiveRuns));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.Integrations WHERE Id = @p0", integrationId));

        Assert.Empty(await Provider().DeleteAsync(workspaceId, integrationId, allowActiveRuns: true, CancellationToken.None));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.Integrations WHERE Id = @p0", integrationId));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.WorkflowIntegrationRefs WHERE IntegrationId = @p0", integrationId));
    }

    [SkippableFact]
    public async Task Finished_runs_do_not_hold_up_the_delete()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        var (integrationId, definitionId, workflowRef, _) = await SeedAsync(workspaceId);
        await AddRunAsync(definitionId, workflowRef, "Succeeded");

        Assert.Empty(await Provider().DeleteAsync(workspaceId, integrationId, allowActiveRuns: false, CancellationToken.None));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.Integrations WHERE Id = @p0", integrationId));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>An integration, and one published version of a disabled workflow with a step using it.</summary>
    private async Task<(int IntegrationId, int DefinitionId, Guid WorkflowRef, Guid StepId)> SeedAsync(int workspaceId)
    {
        var definitions = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        Guid workflowRef = Guid.NewGuid(), stepId = Guid.NewGuid();
        var version = await definitions.InsertAsync(new()
        {
            Id = 0,
            RefId = workflowRef,
            Version = 0,
            WorkspaceId = workspaceId,
            Name = "integration-delete-test",
            Description = null,
            DefinitionJson = """{"nodes":[],"edges":[]}""",
            PublishedBy = 1,
            CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);

        int integrationId = Convert.ToInt32(await ScalarAsync("""
            INSERT INTO dbo.Integrations (RefId, WorkspaceId, Type, Name, ConfigJson, SecretCiphertext, KeyId, CreatedBy)
            OUTPUT INSERTED.Id VALUES (NEWID(), @p0, 4, N'ntfy', N'{}', 0x00, 1, 1);
            """, workspaceId));
        await ScalarAsync("""
            INSERT INTO dbo.WorkflowIntegrationRefs (WorkflowDefinitionId, NodeId, NodeName, IntegrationId)
            VALUES (@p0, @p1, N'notify', @p2);
            """, version.Id, stepId, integrationId);
        return (integrationId, version.Id, workflowRef, stepId);
    }

    private Task<object?> AddRunAsync(int definitionId, Guid workflowRef, string status) => ScalarAsync("""
        INSERT INTO dbo.Runs (RefId, WorkflowDefinitionId, WorkflowRefId, WorkflowVersion, TriggerNodeId, Status, StartedAt, CompletedAt, CreditBudget)
        VALUES (NEWID(), @p0, @p1, 1, NEWID(), @p2, SYSUTCDATETIME(),
                CASE WHEN @p2 IN (N'Running', N'Failing', N'Cancelling') THEN NULL ELSE SYSUTCDATETIME() END, 0);
        """, definitionId, workflowRef, status);

    private IIntegrationProvider Provider() =>
        new IntegrationProvider(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString })
            .Build());

    private async Task<int> CountAsync(string sql, params object[] args) => Convert.ToInt32(await ScalarAsync(sql, args));

    private async Task<object?> ScalarAsync(string sql, params object[] args)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        for (int i = 0; i < args.Length; i++)
        {
            cmd.Parameters.AddWithValue($"@p{i}", args[i]);
        }

        return await cmd.ExecuteScalarAsync();
    }
}

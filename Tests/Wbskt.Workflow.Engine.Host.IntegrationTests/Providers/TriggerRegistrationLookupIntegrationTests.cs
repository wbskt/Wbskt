using System.Data;
using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Channels;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;
using Wbskt.Workflow.Providers;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// dbo.TriggerRegistration_GetAllByKeys (#940): a manual trigger's key is the workflow ref alone, so a
/// lookup that names a workspace sees only that workspace's registrations.
/// </summary>
[Collection("SqlEdge")]
public sealed class TriggerRegistrationLookupIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";

    [SkippableFact]
    public async Task A_lookup_for_a_workspace_finds_only_that_workspaces_registrations()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue - 1);
        Guid workflowRef = Guid.NewGuid();
        string key = ManualChannel.Key(workflowRef.ToString());
        await ProcAsync("dbo.WorkflowDefinition_Publish",
            ("@RefId", workflowRef), ("@WorkspaceId", workspaceId), ("@Name", "wf"), ("@Description", ""),
            ("@DefinitionJson", "{}"), ("@PublishedBy", 1));
        await LiveWorkflows.EnableAsync(fixture.ConnectionString, workflowRef, workspaceId,
            plan: LiveWorkflows.Registration(Guid.NewGuid(), ManualChannel.Name, key, "AllowParallel"));
        var provider = new TriggerRegistrationProvider(ProviderFactory.BuildConfiguration(fixture.ConnectionString));

        var any = await provider.GetActiveByChannelKeysAsync(ManualChannel.Name, [key], null, CancellationToken.None);
        var own = await provider.GetActiveByChannelKeysAsync(ManualChannel.Name, [key], workspaceId, CancellationToken.None);
        var other = await provider.GetActiveByChannelKeysAsync(ManualChannel.Name, [key], workspaceId + 1, CancellationToken.None);

        Assert.Equal(workflowRef, Assert.Single(any).WorkflowRefId);
        Assert.Equal(workflowRef, Assert.Single(own).WorkflowRefId);
        Assert.Empty(other);
    }

    private async Task ProcAsync(string procedure, params (string Name, object Value)[] parameters)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
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
}

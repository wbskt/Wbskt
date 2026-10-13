using Microsoft.Data.SqlClient;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// <c>dbo.WorkflowDefinition_Publish</c>'s per-workspace workflow ceiling: only a first version counts
/// against it, a deleted workflow frees its place, and concurrent first publishes cannot overshoot it.
/// </summary>
[Collection("SqlEdge")]
public sealed class WorkflowCeilingIntegrationTests(SqlEdgeFixture fixture)
{
    private const string Skipped = "SQL Server is not reachable.";
    private const int WorkflowLimitReachedError = 50024;

    [SkippableFact]
    public async Task A_full_workspace_refuses_a_new_workflow_but_not_a_new_version()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var provider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        Guid first = Guid.NewGuid();

        await provider.InsertAsync(Row(first, workspaceId), [], maxWorkflows: 2, CancellationToken.None);
        await provider.InsertAsync(Row(Guid.NewGuid(), workspaceId), [], maxWorkflows: 2, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<SqlException>(() => provider.InsertAsync(Row(Guid.NewGuid(), workspaceId), [], maxWorkflows: 2, CancellationToken.None));
        Assert.Equal(WorkflowLimitReachedError, ex.Number);

        var v2 = await provider.InsertAsync(Row(first, workspaceId), [], maxWorkflows: 2, CancellationToken.None);
        Assert.Equal(2, v2.Version);

        // Another workspace is unaffected, and no ceiling means none.
        await provider.InsertAsync(Row(Guid.NewGuid(), workspaceId - 1), [], maxWorkflows: 2, CancellationToken.None);
        await provider.InsertAsync(Row(Guid.NewGuid(), workspaceId), [], maxWorkflows: null, CancellationToken.None);
    }

    [SkippableFact]
    public async Task Deleting_a_workflow_frees_its_place()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var provider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);
        Guid doomed = Guid.NewGuid();

        await provider.InsertAsync(Row(doomed, workspaceId), [], maxWorkflows: 1, CancellationToken.None);
        await Assert.ThrowsAsync<SqlException>(() => provider.InsertAsync(Row(Guid.NewGuid(), workspaceId), [], maxWorkflows: 1, CancellationToken.None));

        Assert.NotNull(await provider.DeleteAsync(doomed, workspaceId, 9, CancellationToken.None));

        await provider.InsertAsync(Row(Guid.NewGuid(), workspaceId), [], maxWorkflows: 1, CancellationToken.None);
    }

    [SkippableFact]
    public async Task Concurrent_first_publishes_cannot_overshoot_the_ceiling()
    {
        Skip.IfNot(fixture.IsAvailable, Skipped);
        var provider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        int workspaceId = Random.Shared.Next(1_000_000, int.MaxValue);

        Task[] attempts = Enumerable.Range(0, 12)
            .Select(_ => Task.Run(() => provider.InsertAsync(Row(Guid.NewGuid(), workspaceId), [], maxWorkflows: 3, CancellationToken.None)))
            .ToArray();
        try
        {
            await Task.WhenAll(attempts);
        }
        catch (SqlException)
        {
            // Expected for the attempts past the ceiling; each is checked below.
        }

        Assert.All(
            attempts.Where(t => t.IsFaulted),
            t => Assert.Equal(WorkflowLimitReachedError, Assert.IsType<SqlException>(t.Exception!.InnerException).Number));
        Assert.Equal(3, attempts.Count(t => t.IsCompletedSuccessfully));
    }

    private static WorkflowDefinitionRow Row(Guid refId, int workspaceId) => new()
    {
        Id = 0,
        RefId = refId,
        Version = 0,
        WorkspaceId = workspaceId,
        Name = "ceiling-test",
        Description = null,
        IsEnabled = true,
        DefinitionJson = """{"nodes":[],"edges":[]}""",
        PublishedBy = 1,
        CreatedAt = DateTime.UtcNow
    };
}

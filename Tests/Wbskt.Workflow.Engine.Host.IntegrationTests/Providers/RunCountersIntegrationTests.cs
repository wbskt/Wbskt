using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Engine.Host.IntegrationTests.Infrastructure;

namespace Wbskt.Workflow.Engine.Host.IntegrationTests.Providers;

/// <summary>
/// Task 13.3 — RunCounters: verify the atomic OUTPUT clause guarantees that exactly
/// one concurrent decrement observes ActiveBranchCount == 0 (the "last branch" race).
/// </summary>
[Collection("SqlEdge")]
public sealed class RunCountersIntegrationTests(SqlEdgeFixture fixture)
{
    private async Task<int> CreateRunAsync()
    {
        var wdProvider = ProviderFactory.WorkflowDefinition(fixture.ConnectionString);
        WorkflowDefinitionRow wd = await wdProvider.InsertAsync(new WorkflowDefinitionRow
        {
            Id = 0, RefId = Guid.NewGuid(), Version = 0, WorkspaceId = 1,
            Name = $"IT-WD-{Guid.NewGuid():N}",
            Description = null, IsEnabled = true,
            DefinitionJson = """{"nodes":[],"edges":[]}""",
            PublishedBy = 1, CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);

        var runProvider = ProviderFactory.Run(fixture.ConnectionString);
        RunRow run = await runProvider.CreateAsync(new RunRow
        {
            Id = 0, RefId = Guid.NewGuid(),
            WorkflowDefinitionId = wd.Id,
            WorkflowRefId = wd.RefId,
            WorkflowVersion = wd.Version,
            TriggerNodeId = Guid.NewGuid(),
            CorrelationKey = null,
            Status = "Running",
            StartedAt = DateTime.UtcNow,
            CompletedAt = null,
            CancellationRequestedAt = null,
            CancellationReason = null,
            CreditBudget = 0m,
            CreatedAt = DateTime.UtcNow
        }, CancellationToken.None);

        return run.Id;
    }

    [SkippableFact]
    public async Task IncrementActiveBranches_returns_post_update_value()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        int runId = await CreateRunAsync();
        var provider = ProviderFactory.RunCounters(fixture.ConnectionString);

        int result1 = await provider.IncrementActiveBranchesAsync(runId, 1, CancellationToken.None);
        int result2 = await provider.IncrementActiveBranchesAsync(runId, 1, CancellationToken.None);
        int result3 = await provider.IncrementActiveBranchesAsync(runId, -1, CancellationToken.None);

        result1.Should().Be(1);
        result2.Should().Be(2);
        result3.Should().Be(1);
    }

    [SkippableFact]
    public async Task Concurrent_decrements_exactly_one_caller_observes_zero()
    {
        Skip.IfNot(fixture.IsAvailable, "SQL Edge is not available - start it with sa/Welcome1234 on port 1433, or set WBSKT_INTEGRATION_CONNSTR.");

        const int branchCount = 20;

        int runId = await CreateRunAsync();
        var seedProvider = ProviderFactory.RunCounters(fixture.ConnectionString);

        // Seed the counter to branchCount
        await seedProvider.IncrementActiveBranchesAsync(runId, branchCount, CancellationToken.None);

        // Decrement concurrently from branchCount tasks; exactly one should observe 0
        int[] results = new int[branchCount];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, branchCount),
            new ParallelOptions { MaxDegreeOfParallelism = 10 },
            async (i, ct) =>
            {
                var p = ProviderFactory.RunCounters(fixture.ConnectionString);
                results[i] = await p.IncrementActiveBranchesAsync(runId, -1, ct);
            });

        results.Should().NotContain(r => r < 0, "counter must never go negative");
        results.Count(r => r == 0).Should().Be(1, "exactly one branch should observe ActiveBranchCount == 0");
    }
}

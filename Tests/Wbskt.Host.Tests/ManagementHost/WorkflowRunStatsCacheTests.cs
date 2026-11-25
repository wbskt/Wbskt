using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Host.Tests.ManagementHost;

/// <summary>
/// Run stats are cached for half a minute per workspace and window: what is read once, what is read
/// again, and that the workflow's ownership is still checked on every call.
/// </summary>
public sealed class WorkflowRunStatsCacheTests
{
    private const int WorkspaceId = 7;

    private static readonly DateTimeOffset From = new(2026, 11, 1, 0, 0, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 11, 1, 1, 0, 7, TimeSpan.Zero);

    [Fact]
    public async Task Requests_for_the_same_half_minute_read_the_stats_once()
    {
        var runs = WorkspaceRuns();
        var service = CreateService(runs.Object, Mock.Of<IWorkflowDefinitionProvider>(), CreateCache());

        var first = await service.GetWorkspaceStatsAsync(WorkspaceId, From, To, CancellationToken.None);
        var second = await service.GetWorkspaceStatsAsync(WorkspaceId, From.AddSeconds(10), To.AddSeconds(5), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value.Counts, second.Value.Counts);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), second.Value.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 1, 0, 30, TimeSpan.Zero), second.Value.ToUtc);
        runs.Verify(x => x.GetWorkspaceStatsAsync(WorkspaceId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Another_workspace_or_window_is_read_on_its_own()
    {
        var runs = WorkspaceRuns();
        var service = CreateService(runs.Object, Mock.Of<IWorkflowDefinitionProvider>(), CreateCache());

        await service.GetWorkspaceStatsAsync(WorkspaceId, From, To, CancellationToken.None);
        await service.GetWorkspaceStatsAsync(WorkspaceId + 1, From, To, CancellationToken.None);
        await service.GetWorkspaceStatsAsync(WorkspaceId, From, To.AddMinutes(1), CancellationToken.None);

        runs.Verify(x => x.GetWorkspaceStatsAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task A_cached_workflow_answer_is_never_served_to_another_workspace()
    {
        var workflowRefId = Guid.NewGuid();
        var runs = new Mock<IRunProvider>();
        runs.Setup(x => x.GetStatsAsync(workflowRefId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stats(total: 3));
        runs.Setup(x => x.GetTopFailuresAsync(workflowRefId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        runs.Setup(x => x.GetNodeTimingsAsync(workflowRefId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var definitions = OwnedWorkflow(workflowRefId);
        var service = CreateService(runs.Object, definitions.Object, CreateCache());

        var owner = await service.GetStatsAsync(WorkspaceId, workflowRefId, From, To, includeSlowestNodes: false, CancellationToken.None);
        var again = await service.GetStatsAsync(WorkspaceId, workflowRefId, From, To, includeSlowestNodes: false, CancellationToken.None);
        var other = await service.GetStatsAsync(WorkspaceId + 1, workflowRefId, From, To, includeSlowestNodes: false, CancellationToken.None);

        Assert.True(owner.IsSuccess);
        Assert.Equal(3, again.Value.Counts.Total);
        Assert.True(other.IsFailure);
        runs.Verify(x => x.GetStatsAsync(workflowRefId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
        definitions.Verify(x => x.FindCurrentByRefIdAsync(workflowRefId, It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Slowest_nodes_are_read_only_when_asked_for_and_cached_apart()
    {
        var workflowRefId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        var runs = new Mock<IRunProvider>();
        runs.Setup(x => x.GetStatsAsync(workflowRefId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stats(total: 3));
        runs.Setup(x => x.GetTopFailuresAsync(workflowRefId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        runs.Setup(x => x.GetNodeTimingsAsync(workflowRefId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new NodeTimingRow { NodeId = nodeId, Executions = 2, FailureCount = 0, AvgDurationMs = 40, MaxDurationMs = 50 }]);
        var service = CreateService(runs.Object, OwnedWorkflow(workflowRefId).Object, CreateCache());

        var plain = await service.GetStatsAsync(WorkspaceId, workflowRefId, From, To, includeSlowestNodes: false, CancellationToken.None);
        var withNodes = await service.GetStatsAsync(WorkspaceId, workflowRefId, From, To, includeSlowestNodes: true, CancellationToken.None);
        var plainAgain = await service.GetStatsAsync(WorkspaceId, workflowRefId, From, To, includeSlowestNodes: false, CancellationToken.None);

        Assert.Empty(plain.Value.SlowestNodes);
        Assert.Equal(nodeId, Assert.Single(withNodes.Value.SlowestNodes).NodeId);
        Assert.Empty(plainAgain.Value.SlowestNodes);
        runs.Verify(x => x.GetNodeTimingsAsync(workflowRefId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        runs.Verify(x => x.GetStatsAsync(workflowRefId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static Mock<IWorkflowDefinitionProvider> OwnedWorkflow(Guid workflowRefId)
    {
        var definitions = new Mock<IWorkflowDefinitionProvider> { CallBase = true };
        definitions.Setup(x => x.FindCurrentByRefIdAsync(workflowRefId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkflowDefinitionRow
            {
                Id = 9,
                RefId = workflowRefId,
                Version = 1,
                WorkspaceId = WorkspaceId,
                Name = "wf",
                Description = null,
                DefinitionJson = "{\"nodes\":[],\"edges\":[]}",
                PublishedBy = 1,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        return definitions;
    }

    private static Mock<IRunProvider> WorkspaceRuns()
    {
        var runs = new Mock<IRunProvider>();
        runs.Setup(x => x.GetWorkspaceStatsAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Stats(total: 4));
        runs.Setup(x => x.GetPerWorkflowStatsAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return runs;
    }

    private static WorkflowRunQueryService CreateService(IRunProvider runs, IWorkflowDefinitionProvider definitions, HybridCache cache) =>
        new(runs, Mock.Of<IBranchProvider>(), definitions, Mock.Of<IWorkflowStateProvider>(), Mock.Of<ILogger<WorkflowRunQueryService>>(), stats: cache);

    private static HybridCache CreateCache()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        return services.BuildServiceProvider().GetRequiredService<HybridCache>();
    }

    private static RunStatsRow Stats(int total) => new()
    {
        TotalRuns = total,
        SucceededCount = total,
        FailedCount = 0,
        PartiallyFailedCount = 0,
        CancelledCount = 0,
        OutOfCreditsCount = 0,
        ActiveCount = 0,
        WaitingCount = 0,
        P50DurationMs = 120,
        P95DurationMs = 900,
        MaxDurationMs = 1500,
        AvgDurationMs = 300
    };
}

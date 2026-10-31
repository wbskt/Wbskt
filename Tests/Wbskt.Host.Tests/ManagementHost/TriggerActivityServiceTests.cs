using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Wbskt.Management.Host.Services.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;

namespace Wbskt.Host.Tests.ManagementHost;

public sealed class TriggerActivityServiceTests
{
    private const int WorkspaceId = 4;
    private static readonly Guid WorkflowRef = Guid.Parse("aaaaaaaa-0000-4000-8000-00000000000a");
    private static readonly Guid NodeId = Guid.Parse("bbbbbbbb-0000-4000-8000-00000000000b");

    private readonly Mock<IWorkflowDefinitionProvider> _definitions = new();
    private readonly Mock<ITriggerDispatchOutcomeProvider> _outcomes = new();

    [Fact]
    public async Task Another_workspaces_workflow_reads_as_not_found()
    {
        _definitions.Setup(d => d.FindCurrentByRefIdAsync(WorkflowRef, It.IsAny<CancellationToken>())).ReturnsAsync(Definition(WorkspaceId + 1));

        var result = await Service().GetRecentAsync(WorkspaceId, WorkflowRef, NodeId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("WORKFLOW_NOT_FOUND", result.Error.Code);
        _outcomes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task The_triggers_outcomes_and_hold_come_back_as_utc()
    {
        var at = new DateTime(2026, 10, 31, 10, 2, 0, DateTimeKind.Unspecified);
        var run = Guid.NewGuid();
        _definitions.Setup(d => d.FindCurrentByRefIdAsync(WorkflowRef, It.IsAny<CancellationToken>())).ReturnsAsync(Definition(WorkspaceId));
        _outcomes.Setup(o => o.GetRecentAsync(WorkflowRef, NodeId, ITriggerDispatchOutcomeProvider.Keep, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            Outcome(at.AddMinutes(1), "StartedRun", run),
            Outcome(at, "Filtered", null)
        ]);
        _outcomes.Setup(o => o.FindHoldStateAsync(WorkflowRef, NodeId, It.IsAny<CancellationToken>())).ReturnsAsync(new TriggerHoldStateRow
        {
            State = "holding",
            SinceAt = at,
            DueAt = at.AddMinutes(10),
            LastEventAt = at.AddMinutes(1)
        });

        var result = await Service().GetRecentAsync(WorkspaceId, WorkflowRef, NodeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["StartedRun", "Filtered"], result.Value.Recent.Select(o => o.Outcome));
        Assert.Equal(run, result.Value.Recent[0].RunRef);
        Assert.Equal("temperature", result.Value.Recent[0].MessageType);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 10, 2, 0, TimeSpan.Zero), result.Value.Recent[1].OccurredAt);
        Assert.NotNull(result.Value.Hold);
        Assert.Equal("holding", result.Value.Hold!.State);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 10, 12, 0, TimeSpan.Zero), result.Value.Hold.DueAt);
    }

    [Fact]
    public async Task A_trigger_without_a_hold_has_none()
    {
        _definitions.Setup(d => d.FindCurrentByRefIdAsync(WorkflowRef, It.IsAny<CancellationToken>())).ReturnsAsync(Definition(WorkspaceId));
        _outcomes.Setup(o => o.GetRecentAsync(WorkflowRef, NodeId, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await Service().GetRecentAsync(WorkspaceId, WorkflowRef, NodeId, CancellationToken.None);

        Assert.Empty(result.Value.Recent);
        Assert.Null(result.Value.Hold);
    }

    private TriggerActivityService Service() => new(_definitions.Object, _outcomes.Object, NullLogger<TriggerActivityService>.Instance);

    private static TriggerDispatchOutcomeRow Outcome(DateTime at, string outcome, Guid? run) => new()
    {
        WorkflowRefId = WorkflowRef,
        TriggerNodeId = NodeId,
        OccurredAt = at,
        ChannelKind = "client",
        MessageType = "temperature",
        Outcome = outcome,
        CorrelationKey = "client:x:temperature",
        RunRefId = run
    };

    private static WorkflowDefinitionRow Definition(int workspaceId) => new()
    {
        Id = 9,
        RefId = WorkflowRef,
        Version = 1,
        WorkspaceId = workspaceId,
        Name = "wf",
        Description = null,
        IsEnabled = true,
        DefinitionJson = "{\"nodes\":[],\"edges\":[]}",
        PublishedBy = 1,
        CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };
}

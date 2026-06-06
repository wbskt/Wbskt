using Moq;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Models;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.InboundAdapters;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

public sealed class EventBusRunLifecyclePublisherTests
{
    private static readonly Guid WorkflowRefId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid RunRefId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const int WorkflowDefinitionId = 7;
    private const int WorkspaceId = 42;

    [Fact]
    public async Task StartedPublisher_publishes_started_event_with_resolved_workspace()
    {
        var bus = new Mock<IEventBus>();
        WorkflowRunStartedEvent? captured = null;
        bus.Setup(b => b.PublishAsync(It.IsAny<WorkflowRunStartedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowRunStartedEvent, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var publisher = new EventBusRunStartedPublisher(CreateCache(), bus.Object);

        await publisher.PublishAsync(CreateRun(), CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(WorkflowRefId, captured!.WorkflowRefId);
        Assert.Equal(WorkflowDefinitionId, captured.WorkflowId);
        Assert.Equal(RunRefId, captured.RunRefId);
        Assert.Equal(WorkspaceId, captured.WorkspaceId);
    }

    [Theory]
    [InlineData("Succeeded")]
    [InlineData("Cancelled")]
    public async Task CompletedPublisher_publishes_completed_event_for_non_failure_status(string status)
    {
        var bus = new Mock<IEventBus>();
        WorkflowRunCompletedEvent? captured = null;
        bus.Setup(b => b.PublishAsync(It.IsAny<WorkflowRunCompletedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowRunCompletedEvent, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var publisher = new EventBusRunCompletedPublisher(CreateCache(), bus.Object);

        await publisher.PublishAsync(CreateRun(), status, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(status, captured!.Status);
        Assert.Equal(WorkspaceId, captured.WorkspaceId);
        Assert.Equal(WorkflowRefId, captured.WorkflowRefId);
        bus.Verify(b => b.PublishAsync(It.IsAny<WorkflowRunFailedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("PartiallyFailed")]
    public async Task CompletedPublisher_publishes_failed_event_for_failure_status(string status)
    {
        var bus = new Mock<IEventBus>();
        WorkflowRunFailedEvent? captured = null;
        bus.Setup(b => b.PublishAsync(It.IsAny<WorkflowRunFailedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<WorkflowRunFailedEvent, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        var publisher = new EventBusRunCompletedPublisher(CreateCache(), bus.Object);

        await publisher.PublishAsync(CreateRun(), status, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(status, captured!.Status);
        Assert.Equal(WorkspaceId, captured.WorkspaceId);
        bus.Verify(b => b.PublishAsync(It.IsAny<WorkflowRunCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static IWorkflowDefinitionCache CreateCache()
    {
        var definition = new WorkflowDefinition(
            WorkflowRefId, 1, WorkspaceId, "test", null, true, [], [], [], DateTime.UtcNow, 1);
        var cache = new Mock<IWorkflowDefinitionCache>();
        cache.Setup(c => c.GetAsync(WorkflowDefinitionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);
        return cache.Object;
    }

    private static RunRow CreateRun()
    {
        return new RunRow
        {
            Id = 1,
            RefId = RunRefId,
            WorkflowDefinitionId = WorkflowDefinitionId,
            WorkflowRefId = WorkflowRefId,
            WorkflowVersion = 1,
            TriggerNodeId = Guid.NewGuid(),
            CorrelationKey = null,
            Status = "Running",
            StartedAt = DateTime.UtcNow,
            CompletedAt = null,
            CancellationRequestedAt = null,
            CancellationReason = null,
            CreditBudget = 0m,
            CreatedAt = DateTime.UtcNow
        };
    }
}

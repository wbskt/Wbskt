using MassTransit;
using Moq;
using Wbskt.Events.Workflow;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.InboundAdapters;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

public sealed class WorkflowRunCancellationRequestedEventConsumerTests
{
    [Fact]
    public async Task Consume_requests_cancellation_and_cancels_cts()
    {
        var cancellationService = new Mock<IRunCancellationService>();
        cancellationService.Setup(s => s.RequestCancellationAsync(42, "reason", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        WorkflowRunCancellationRequestedEvent evt = new(42, "reason");
        var context = new Mock<ConsumeContext<WorkflowRunCancellationRequestedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        var consumer = new WorkflowRunCancellationRequestedEventConsumer(cancellationService.Object);

        await consumer.Consume(context.Object);

        cancellationService.Verify(s => s.RequestCancellationAsync(42, "reason", CancellationToken.None), Times.Once);
        cancellationService.Verify(s => s.CancelCts(42), Times.Once);
    }

    [Fact]
    public async Task Consume_still_cancels_cts_when_run_is_already_cancelling()
    {
        // RequestCancellationAsync is idempotent and returns false once the sender (e.g. the
        // management host) already transitioned the run - but CancelCts must still fire
        // unconditionally so this host's local token is cancelled regardless.
        var cancellationService = new Mock<IRunCancellationService>();
        cancellationService.Setup(s => s.RequestCancellationAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        WorkflowRunCancellationRequestedEvent evt = new(42, "reason");
        var context = new Mock<ConsumeContext<WorkflowRunCancellationRequestedEvent>>();
        context.SetupGet(c => c.Message).Returns(evt);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        var consumer = new WorkflowRunCancellationRequestedEventConsumer(cancellationService.Object);

        await consumer.Consume(context.Object);

        cancellationService.Verify(s => s.CancelCts(42), Times.Once);
    }
}

using MassTransit;
using Moq;
using Wbskt.Events.Workflow;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.InboundAdapters;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

public sealed class WorkflowRunCancellationRequestedEventConsumerTests
{
    [Fact]
    public async Task Consume_invalidates_the_local_cancellation_view_before_touching_the_database()
    {
        // This is the whole point of the event: cancellation is read through a per-host cache, so a
        // cancel issued on another host is invisible here until this fires.
        var harness = new Harness();

        await harness.Consumer.Consume(harness.Context(42, "reason"));

        Assert.Equal(["mark", "request"], harness.Calls);
    }

    [Fact]
    public async Task Consume_applies_the_cancellation_even_when_another_host_already_transitioned_the_run()
    {
        // The sender transitioned the status but has no finalizer and does not own this host's tokens;
        // the engine still has to do its half, so the call is unconditional.
        var harness = new Harness();
        harness.CancellationService.Setup(s => s.RequestCancellationAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await harness.Consumer.Consume(harness.Context(42, "reason"));

        harness.CancellationService.Verify(s => s.RequestCancellationAsync(42, "reason", CancellationToken.None), Times.Once);
        harness.CancellationService.Verify(s => s.MarkCancellationRequested(42), Times.Once);
    }

    [Fact]
    public async Task Consume_tolerates_a_run_that_is_already_terminal()
    {
        var harness = new Harness();
        harness.CancellationService.Setup(s => s.RequestCancellationAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await harness.Consumer.Consume(harness.Context(42, "reason"));

        harness.CancellationService.Verify(s => s.MarkCancellationRequested(42), Times.Once);
    }

    private sealed class Harness
    {
        public Mock<IRunCancellationService> CancellationService { get; } = new();
        public List<string> Calls { get; } = [];
        public WorkflowRunCancellationRequestedEventConsumer Consumer { get; }

        public Harness()
        {
            CancellationService.Setup(s => s.MarkCancellationRequested(It.IsAny<long>()))
                .Callback(() => Calls.Add("mark"));
            CancellationService.Setup(s => s.RequestCancellationAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback(() => Calls.Add("request"))
                .ReturnsAsync(true);
            Consumer = new WorkflowRunCancellationRequestedEventConsumer(CancellationService.Object);
        }

        public ConsumeContext<WorkflowRunCancellationRequestedEvent> Context(long runId, string reason)
        {
            var context = new Mock<ConsumeContext<WorkflowRunCancellationRequestedEvent>>();
            context.SetupGet(c => c.Message).Returns(new WorkflowRunCancellationRequestedEvent(runId, reason));
            context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
            return context.Object;
        }
    }
}

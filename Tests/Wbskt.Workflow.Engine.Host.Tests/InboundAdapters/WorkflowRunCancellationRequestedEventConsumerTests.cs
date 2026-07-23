using MassTransit;
using Moq;
using Wbskt.Events.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Engine.Host.InboundAdapters;

namespace Wbskt.Workflow.Engine.Host.Tests.InboundAdapters;

public sealed class WorkflowRunCancellationRequestedEventConsumerTests
{
    [Fact]
    public async Task Consume_requests_cancellation_and_cancels_cts()
    {
        var harness = new Harness(runStatus: "Running");
        harness.CancellationService.Setup(s => s.RequestCancellationAsync(42, "reason", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await harness.Consumer.Consume(harness.Context(42, "reason"));

        harness.CancellationService.Verify(s => s.RequestCancellationAsync(42, "reason", CancellationToken.None), Times.Once);
        harness.CancellationService.Verify(s => s.CancelCts(42), Times.Once);
    }

    [Fact]
    public async Task Consume_still_cancels_cts_when_run_is_already_cancelling()
    {
        // RequestCancellationAsync is idempotent and returns false once the sender (e.g. the
        // management host) already transitioned the run - but CancelCts must still fire
        // unconditionally so this host's local token is cancelled regardless.
        var harness = new Harness(runStatus: "Cancelling", activeBranchCount: 1);
        harness.CancellationService.Setup(s => s.RequestCancellationAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await harness.Consumer.Consume(harness.Context(42, "reason"));

        harness.CancellationService.Verify(s => s.CancelCts(42), Times.Once);
        // A branch is still executing, so finalization is left to the branch loop.
        harness.Finalizer.Verify(f => f.FinalizeAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Consume_finalizes_idle_cancelling_run()
    {
        // The parked run's waiting branch was already cancelled by the sender, so ActiveBranchCount
        // is 0 and no live branch will finalize it - the engine must complete the transition.
        var harness = new Harness(runStatus: "Cancelling", activeBranchCount: 0);
        harness.CancellationService.Setup(s => s.RequestCancellationAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await harness.Consumer.Consume(harness.Context(42, "reason"));

        harness.Finalizer.Verify(f => f.FinalizeAsync(42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Consume_does_not_finalize_non_cancelling_run()
    {
        var harness = new Harness(runStatus: "Running", activeBranchCount: 0);
        harness.CancellationService.Setup(s => s.RequestCancellationAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await harness.Consumer.Consume(harness.Context(42, "reason"));

        harness.Finalizer.Verify(f => f.FinalizeAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class Harness
    {
        public Mock<IRunCancellationService> CancellationService { get; } = new();
        public Mock<IRunProvider> RunProvider { get; } = new();
        public Mock<IRunCountersProvider> Counters { get; } = new();
        public Mock<IRunFinalizer> Finalizer { get; } = new();
        public WorkflowRunCancellationRequestedEventConsumer Consumer { get; }

        public Harness(string runStatus, int activeBranchCount = 0)
        {
            RunProvider.Setup(p => p.GetByIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((long id, CancellationToken _) => CreateRun(id, runStatus));
            Counters.Setup(c => c.GetByRunIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int id, CancellationToken _) => new RunCountersRow { RunId = id, ActiveBranchCount = activeBranchCount, CreditsConsumed = 0, UpdatedAt = DateTime.UtcNow });
            Consumer = new WorkflowRunCancellationRequestedEventConsumer(CancellationService.Object, RunProvider.Object, Counters.Object, Finalizer.Object);
        }

        public ConsumeContext<WorkflowRunCancellationRequestedEvent> Context(long runId, string reason)
        {
            var context = new Mock<ConsumeContext<WorkflowRunCancellationRequestedEvent>>();
            context.SetupGet(c => c.Message).Returns(new WorkflowRunCancellationRequestedEvent(runId, reason));
            context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
            return context.Object;
        }

        private static RunRow CreateRun(long id, string status) => new()
        {
            Id = checked((int)id),
            RefId = Guid.NewGuid(),
            WorkflowDefinitionId = 1,
            WorkflowRefId = Guid.NewGuid(),
            WorkflowVersion = 1,
            TriggerNodeId = Guid.NewGuid(),
            CorrelationKey = null,
            Status = status,
            StartedAt = DateTime.UtcNow,
            CompletedAt = null,
            CancellationRequestedAt = null,
            CancellationReason = null,
            CreditBudget = 0,
            CreatedAt = DateTime.UtcNow
        };
    }
}

using MassTransit;
using Wbskt.Events.Workflow;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

public sealed class WorkflowRunCancellationRequestedEventConsumer : IConsumer<WorkflowRunCancellationRequestedEvent>
{
    private readonly IRunCancellationService _runCancellationService;

    public WorkflowRunCancellationRequestedEventConsumer(IRunCancellationService runCancellationService)
    {
        _runCancellationService = runCancellationService;
    }

    public async Task Consume(ConsumeContext<WorkflowRunCancellationRequestedEvent> context)
    {
        // The engine host is authoritative for cancellation regardless of whether the sender
        // (e.g. the management host) already transitioned the DB status: RequestCancellationAsync
        // is idempotent (returns false if the run isn't Running), so it's safe to call
        // unconditionally here, and CancelCts always fires so the local CTS is cancelled even when
        // this host's own RequestCancellationAsync call already handled the transition.
        await _runCancellationService.RequestCancellationAsync(context.Message.RunId, context.Message.Reason, context.CancellationToken);
        _runCancellationService.CancelCts(context.Message.RunId);
    }
}

using MassTransit;
using Wbskt.Events.Workflow;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

public sealed class WorkflowRunCancellationRequestedEventConsumer : IConsumer<WorkflowRunCancellationRequestedEvent>
{
    private readonly IRunCancellationService _runCancellationService;
    private readonly ILogger<WorkflowRunCancellationRequestedEventConsumer>? _logger;

    public WorkflowRunCancellationRequestedEventConsumer(
        IRunCancellationService runCancellationService,
        ILogger<WorkflowRunCancellationRequestedEventConsumer>? logger = null)
    {
        _runCancellationService = runCancellationService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<WorkflowRunCancellationRequestedEvent> context)
    {
        long runId = context.Message.RunId;
        CancellationToken ct = context.CancellationToken;

        // First, unconditionally: cancellation is read through a short per-host cache, so until this host
        // is told, its own IsCancellationRequestedAsync keeps answering with whatever it cached before the
        // cancel. This is what makes a cancel issued through the management host visible here immediately
        // rather than up to a cache window later.
        _runCancellationService.MarkCancellationRequested(runId);

        // Then the durable work. The engine host is authoritative for cancellation regardless of whether
        // the sender already transitioned the DB status: RequestCancellationAsync is idempotent, and on
        // the already-'Cancelling' path it still deletes bookmarks, cancels waiting branches and - since
        // this host is the one with an IRunFinalizer - finalizes a run left with nothing running. Without
        // that a parked, cancelled run would sit in 'Cancelling' until the 30-minute reaper.
        bool cancelling = await _runCancellationService.RequestCancellationAsync(runId, context.Message.Reason, ct);
        if (!cancelling)
        {
            _logger?.LogDebug("Run {RunId} is already terminal; nothing left to cancel.", runId);
        }
    }
}

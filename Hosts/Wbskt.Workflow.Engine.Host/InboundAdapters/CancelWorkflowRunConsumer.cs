using MassTransit;
using Wbskt.Events.Workflow;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

/// <summary>
/// Carries out a <see cref="CancelWorkflowRun"/> sent by the management host, which no longer cancels
/// in process: the engine is the only writer of run state. One shared queue, so one engine instance
/// handles each command (see <see cref="CancelWorkflowRunConsumerDefinition"/>).
/// </summary>
/// <remarks>
/// Idempotent, so a redelivery is harmless. <see cref="IRunCancellationService.RequestCancellationAsync"/>
/// does nothing to a terminal run, and for a run already 'Cancelling' it only repeats its cleanup
/// (bookmarks, waiting branches, finalize when idle), without a second history event or announcement.
/// </remarks>
public sealed class CancelWorkflowRunConsumer : IConsumer<CancelWorkflowRun>
{
    private readonly IRunCancellationService _runCancellationService;
    private readonly ILogger<CancelWorkflowRunConsumer>? _logger;

    public CancelWorkflowRunConsumer(
        IRunCancellationService runCancellationService,
        ILogger<CancelWorkflowRunConsumer>? logger = null)
    {
        _runCancellationService = runCancellationService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CancelWorkflowRun> context)
    {
        long runId = context.Message.RunId;

        // The management host has already refused a run it read as terminal, but the run can still
        // finish between that read and this delivery. That is not an error: there is nothing to cancel.
        bool cancelling = await _runCancellationService.RequestCancellationAsync(runId, context.Message.Reason, context.CancellationToken);
        if (!cancelling)
        {
            _logger?.LogDebug("Run {RunId} is already terminal; nothing left to cancel.", runId);
            return;
        }

        _logger?.LogInformation("Run {RunId} is cancelling (Reason: '{Reason}').", runId, context.Message.Reason);
    }
}

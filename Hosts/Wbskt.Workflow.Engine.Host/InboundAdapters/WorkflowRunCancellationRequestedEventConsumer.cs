using MassTransit;
using Wbskt.Events.Workflow;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

public sealed class WorkflowRunCancellationRequestedEventConsumer : IConsumer<WorkflowRunCancellationRequestedEvent>
{
    private readonly IRunCancellationService _runCancellationService;
    private readonly IRunProvider _runProvider;
    private readonly IRunCountersProvider _runCountersProvider;
    private readonly IRunFinalizer _runFinalizer;
    private readonly ILogger<WorkflowRunCancellationRequestedEventConsumer>? _logger;

    public WorkflowRunCancellationRequestedEventConsumer(
        IRunCancellationService runCancellationService,
        IRunProvider runProvider,
        IRunCountersProvider runCountersProvider,
        IRunFinalizer runFinalizer,
        ILogger<WorkflowRunCancellationRequestedEventConsumer>? logger = null)
    {
        _runCancellationService = runCancellationService;
        _runProvider = runProvider;
        _runCountersProvider = runCountersProvider;
        _runFinalizer = runFinalizer;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<WorkflowRunCancellationRequestedEvent> context)
    {
        long runId = context.Message.RunId;
        CancellationToken ct = context.CancellationToken;

        // The engine host is authoritative for cancellation regardless of whether the sender
        // (e.g. the management host) already transitioned the DB status: RequestCancellationAsync
        // is idempotent (returns false if the run isn't Running), so it's safe to call
        // unconditionally here, and CancelCts always fires so the local CTS is cancelled even when
        // this host's own RequestCancellationAsync call already handled the transition.
        await _runCancellationService.RequestCancellationAsync(runId, context.Message.Reason, ct);
        _runCancellationService.CancelCts(runId);

        // A run parked on a bookmark has no live branch to drive its own finalization: the sender
        // transitioned it to Cancelling and cancelled its (parked, still-counted) waiting branch, but
        // the management host has no IRunFinalizer and this host's RequestCancellationAsync
        // short-circuited because the status was already Cancelling. So the run would sit in
        // Cancelling until the 30-minute RunReaper swept it. Once no active branches remain, the
        // engine - which has the finalizer - completes the terminal transition here. A run whose
        // branch is still executing keeps ActiveBranchCount > 0 and is left for the branch loop to
        // finalize when that branch unwinds; FinalizeAsync is idempotent, so either path is safe.
        RunRow run = await _runProvider.GetByIdAsync(runId, ct);
        if (!string.Equals(run.Status, "Cancelling", StringComparison.Ordinal))
        {
            return;
        }

        RunCountersRow counters = await _runCountersProvider.GetByRunIdAsync(checked((int)runId), ct);
        if (counters.ActiveBranchCount <= 0)
        {
            _logger?.LogInformation("Finalizing idle Cancelling run {RunId} after cancellation (no active branches).", runId);
            await _runFinalizer.FinalizeAsync(runId, ct);
        }
    }
}

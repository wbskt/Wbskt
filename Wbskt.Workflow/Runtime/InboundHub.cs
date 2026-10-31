using Microsoft.Extensions.Logging;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Telemetry;

namespace Wbskt.Workflow.Runtime;

internal sealed class InboundHub : IInboundHub
{
    private readonly ITriggerDispatcher _triggerDispatcher;
    private readonly TimeProvider _clock;
    private readonly IEngineStartupTracker _startupTracker;
    private readonly ILogger<InboundHub> _logger;
    private readonly WorkflowMetrics? _metrics;

    public InboundHub(
        ITriggerDispatcher triggerDispatcher,
        TimeProvider clock,
        ILogger<InboundHub>? logger = null,
        IEngineStartupTracker? startupTracker = null,
        WorkflowMetrics? metrics = null)
    {
        _metrics = metrics;
        _triggerDispatcher = triggerDispatcher;
        _clock = clock;
        _startupTracker = startupTracker ?? new CompletedEngineStartupTracker();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<InboundHub>.Instance;
    }

    public async Task<TriggerDispatchResult> HandleAsync(InboundEvent evt, CancellationToken ct)
    {
        await _startupTracker.Ready;

        InboundEvent normalizedEvent = evt;
        if (normalizedEvent.ReceivedAt == default)
        {
            normalizedEvent = normalizedEvent with { ReceivedAt = _clock.GetUtcNow().UtcDateTime };
        }

        try
        {
            _logger.LogDebug("Handling inbound event {InboundEventId} on channel {ChannelKind}", normalizedEvent.InboundEventId, normalizedEvent.ChannelKind);

            // Arrivals that produce a run ARE now in that run's trace: RunStarted carries the channel,
            // the match keys and the received-at, and a bookmark wake carries its dispatch reason on
            // BranchResumed. There is nothing left to add here for those.
            //
            // Arrivals that reach a trigger but start nothing (filtered, secret mismatch, dropped,
            // queued) are kept per trigger node by TriggerOutcomeLog, newest 20 only, which bounds
            // both the volume and the access question: each row belongs to one workflow. An arrival
            // that matched nothing has no workspace to scope it to, so it is still only this log line.
            var result = await _triggerDispatcher.DispatchAsync(normalizedEvent, ct);
            // Debug, not Information: this runs for every device message, and at 100 msg/s a line each
            // costs ~11 GB of Loki. The counter keeps the outcome totals visible instead.
            _logger.LogDebug("Dispatched inbound event {InboundEventId} with outcome {Outcome}", normalizedEvent.InboundEventId, result.Outcome);
            _metrics?.RecordInboundEvent(normalizedEvent.ChannelKind, result.Outcome.ToString());
            return result;
        }
        catch (Exception ex)
        {
            _metrics?.RecordInboundEvent(normalizedEvent.ChannelKind, "Failed");
            _logger.LogError(ex, "Inbound event handling failed for {InboundEventId}.", normalizedEvent.InboundEventId);
            throw;
        }
    }
}

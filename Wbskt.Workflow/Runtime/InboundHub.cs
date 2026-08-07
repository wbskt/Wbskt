using Microsoft.Extensions.Logging;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class InboundHub : IInboundHub
{
    private readonly ITriggerDispatcher _triggerDispatcher;
    private readonly IClock _clock;
    private readonly IEngineStartupTracker _startupTracker;
    private readonly ILogger<InboundHub> _logger;

    public InboundHub(
        ITriggerDispatcher triggerDispatcher,
        IClock clock,
        ILogger<InboundHub>? logger = null,
        IEngineStartupTracker? startupTracker = null)
    {
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
            normalizedEvent = normalizedEvent with { ReceivedAt = _clock.UtcNow };
        }

        try
        {
            _logger.LogDebug("Handling inbound event {InboundEventId} on channel {ChannelKind}", normalizedEvent.InboundEventId, normalizedEvent.ChannelKind);

            // Arrivals that produce a run ARE now in that run's trace: RunStarted carries the channel,
            // the match keys and the received-at, and a bookmark wake carries its dispatch reason on
            // BranchResumed. There is nothing left to add here for those.
            //
            // TODO(arch): arrivals that produce NO run - filtered, secret mismatch, dropped, queued, or
            // matching nothing - still have no durable record; this log line is it. They cannot go in
            // HistoryEvents, whose clustered key leads with a NOT NULL RunId, so they need their own
            // table. That is a schema decision with an access question attached (an arrival that matched
            // nothing has no workspace to scope it to, so a per-workspace endpoint over it would leak
            // across workspaces) and a volume one (an insert per inbound event, on the hottest path).
            // The API-level version of the same question is already answered: TriggerDispatchResult
            // reports every registration's outcome, so a caller is told why nothing ran.
            var result = await _triggerDispatcher.DispatchAsync(normalizedEvent, ct);
            _logger.LogInformation("Successfully dispatched inbound event {InboundEventId} with outcome {Outcome}", normalizedEvent.InboundEventId, result.Outcome);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inbound event handling failed for {InboundEventId}.", normalizedEvent.InboundEventId);
            throw;
        }
    }
}

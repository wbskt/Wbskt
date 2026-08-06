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

            // TODO(arch): inbound events are not in the run history, and cannot be as the schema
            // stands. HistoryEvents.RunId is NOT NULL and is the leading column of the clustered
            // primary key, but at this point no run exists yet - dispatch may start one, resume a
            // bookmark, queue, drop, or match nothing at all. Recording only the cases that produced
            // a run would omit exactly the ones an operator needs ("I fired the webhook and nothing
            // happened"). Fixing it properly means either making RunId nullable (a clustered-key
            // change) or giving inbound events their own log; either is a schema decision, not a
            // wiring one. Until then this level is the only record. See design §4.3/§6.1.
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

using Microsoft.Extensions.Logging;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

public sealed class InboundHub : IInboundHub
{
    private readonly ITriggerDispatcher _triggerDispatcher;
    private readonly IClock _clock;
    private readonly ILogger<InboundHub> _logger;

    public InboundHub(ITriggerDispatcher triggerDispatcher, IClock clock, ILogger<InboundHub> logger)
    {
        _triggerDispatcher = triggerDispatcher;
        _clock = clock;
        _logger = logger;
    }

    public async Task<TriggerDispatchResult> HandleAsync(InboundEvent evt, CancellationToken ct)
    {
        InboundEvent normalizedEvent = evt;
        if (normalizedEvent.ReceivedAt == default)
        {
            normalizedEvent = normalizedEvent with { ReceivedAt = _clock.UtcNow };
        }

        try
        {
            // TODO: Append engine-host inbound history entry per Workflow.Engine.V3.Design.md §4.3/§6.1 when Phase 8 history wiring lands.
            return await _triggerDispatcher.DispatchAsync(normalizedEvent, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inbound event handling failed for {InboundEventId}.", normalizedEvent.InboundEventId);
            throw;
        }
    }
}

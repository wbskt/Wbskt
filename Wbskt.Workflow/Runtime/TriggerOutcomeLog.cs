using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// Records what each trigger did with a message, so the console can say why nothing ran (#416).
/// Best effort: a failed record is logged and never fails the dispatch it describes.
/// </summary>
public sealed class TriggerOutcomeLog(ITriggerDispatchOutcomeProvider provider, TimeProvider clock, ILogger<TriggerOutcomeLog> logger)
{
    public async Task RecordAsync(InboundEvent evt, TriggerRegistrationRow registration, string outcome, string? correlationKey, Guid? runRefId, CancellationToken ct)
    {
        try
        {
            await provider.RecordAsync(new TriggerDispatchOutcomeRow
            {
                WorkflowRefId = registration.WorkflowRefId,
                TriggerNodeId = registration.TriggerNodeId,
                // An adapter's event leaves ReceivedAt unset until the hub stamps it; the hold recorder sees it unstamped.
                OccurredAt = evt.ReceivedAt == default ? clock.GetUtcNow().UtcDateTime : evt.ReceivedAt,
                ChannelKind = evt.ChannelKind,
                MessageType = MessageType(evt),
                Outcome = outcome,
                CorrelationKey = correlationKey,
                RunRefId = runRefId
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not record outcome {Outcome} of event {EventId} for trigger {TriggerNodeId}", outcome, evt.InboundEventId, registration.TriggerNodeId);
        }
    }

    // Client messages (and held ones) carry their type; other channels have none.
    private static string? MessageType(InboundEvent evt) =>
        evt.Payload.TryGetValue("messageType", out JsonElement type) && type.ValueKind == JsonValueKind.String ? type.GetString() : null;
}

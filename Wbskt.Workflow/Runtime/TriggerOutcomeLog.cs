using System.Text.Json;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

/// <summary>
/// Records what each trigger did with a message, so the console can say why nothing ran (#416).
/// Only buffers it (<see cref="TriggerOutcomeBuffer"/>); the engine writes the buffer every few
/// seconds, so recording costs the dispatch nothing and can never fail it.
/// </summary>
public sealed class TriggerOutcomeLog(TriggerOutcomeBuffer buffer, TimeProvider clock)
{
    public void Record(InboundEvent evt, TriggerRegistrationRow registration, string outcome, string? correlationKey, Guid? runRefId)
    {
        buffer.Add(new TriggerDispatchOutcomeRow
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
        });
    }

    // Client messages (and held ones) carry their type; other channels have none.
    private static string? MessageType(InboundEvent evt) =>
        evt.Payload.TryGetValue("messageType", out JsonElement type) && type.ValueKind == JsonValueKind.String ? type.GetString() : null;
}

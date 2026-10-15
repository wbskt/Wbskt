using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Channels;

/// <summary>
/// A client trigger with a hold time. Registered under its own kind, so a matching message never
/// starts a run directly: the engine records it against this trigger, and its hold ticker dispatches
/// once the filter has kept matching for the whole hold time. Keys: see <see cref="ClientHoldTriggerKey"/>.
/// </summary>
public sealed class ClientHoldChannel : TriggerChannel
{
    public const string Name = ClientHoldTriggerKey.TriggerKind;

    public override string Kind => Name;

    /// <param name="heldPayload">
    /// The latest matching message's trigger payload, exactly as a plain client trigger would have
    /// seen it; the event adds when the hold began and how long it had to last.
    /// </param>
    public static InboundEvent Event(
        int holdId,
        string triggerKey,
        DateTime sinceAt,
        int holdSeconds,
        IReadOnlyDictionary<string, JsonElement> heldPayload,
        DateTime now)
    {
        var payload = new Dictionary<string, JsonElement>(heldPayload, StringComparer.Ordinal)
        {
            ["heldSince"] = Json(DateTime.SpecifyKind(sinceAt, DateTimeKind.Utc)),
            ["holdSeconds"] = Json(holdSeconds)
        };

        return new InboundEvent(
            Name,
            [triggerKey],
            // One id per hold: a re-leased hold (the process died before it was marked fired) is dropped
            // by the dispatcher's idempotency claim, while a later hold of the same trigger is not.
            $"client-hold:{holdId}:{sinceAt.Ticks}",
            payload,
            now);
    }

    // A held message correlates exactly as the message itself would have.
    public override string CorrelationKey(InboundEvent evt) =>
        ClientChannel.Key(Required(evt, "clientRefId"), Required(evt, "messageType"));

    public override Task<TriggerRegistrationSpec?> RegisterAsync(BaseNode node, TriggerPublication publication, CancellationToken ct)
    {
        if (node is not ClientTriggerNode { Config.HoldSeconds: > 0 } trigger)
        {
            return Task.FromResult<TriggerRegistrationSpec?>(null);
        }

        // The filter is still copied onto the row, both for the recording and for the dispatcher,
        // which checks it again on the held payload.
        ClientTriggerConfig config = trigger.Config;
        string key = ClientHoldTriggerKey.Build(Guid.Parse(config.ClientRef), config.Type, config.HoldSeconds, publication.Definition.RefId, trigger.NodeId);
        return Task.FromResult<TriggerRegistrationSpec?>(new TriggerRegistrationSpec(key, config.ConcurrencyPolicy, config.CorrelationKey) { Filter = config.Filter });
    }
}

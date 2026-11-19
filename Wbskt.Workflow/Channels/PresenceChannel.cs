using Wbskt.Models;
using System.Text.Json;
using Wbskt.Workflow.Abstraction.Models.Nodes;
using Wbskt.Workflow.Abstraction.Models.Nodes.Triggers;
using Wbskt.Workflow.Abstraction.Models.Triggers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Channels;

/// <summary>A client that came online or went offline and stayed that way. Keys: see <see cref="ClientPresenceTriggerKey"/>.</summary>
public sealed class PresenceChannel : TriggerChannel
{
    public const string Name = ClientPresenceTriggerKey.TriggerKind;

    public override string Kind => Name;

    public static InboundEvent Event(
        int checkId,
        string triggerKey,
        Guid clientRefId,
        string state,
        DateTime changedAt,
        int forSeconds,
        DateTime now) => new(
        Name,
        [triggerKey],
        // The check's own id: stable if the process dies between this dispatch and the delete, so
        // the re-leased check is dropped by the dispatcher's idempotency claim instead of running twice.
        $"presence:{checkId}",
        new Dictionary<string, JsonElement>
        {
            ["clientRefId"] = Json(clientRefId),
            ["state"] = Json(state),
            ["changedAt"] = Json(changedAt.AsUtc()),
            ["forSeconds"] = Json(forSeconds)
        },
        now);

    public override string CorrelationKey(InboundEvent evt) =>
        $"{Name}:{Required(evt, "clientRefId")}:{Required(evt, "state")}";

    public override TriggerRegistrationSpec? Plan(BaseNode node, TriggerPublication publication)
    {
        if (node is not ClientPresenceTriggerNode trigger)
        {
            return null;
        }

        ClientPresenceTriggerConfig config = trigger.Config;
        string key = ClientPresenceTriggerKey.Build(Guid.Parse(config.ClientRef), config.State, config.ForSeconds, publication.WorkflowRefId, trigger.NodeId);
        return new TriggerRegistrationSpec(key, config.ConcurrencyPolicy, config.CorrelationKey);
    }
}

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

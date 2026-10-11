using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Runtime;

internal sealed class CorrelationKeyResolver(ITriggerChannelRegistry channels) : ICorrelationKeyResolver
{
    public string Resolve(InboundEvent evt)
    {
        // A kind no channel claims falls back to the key its adapter supplied, so dispatch resolves to
        // NoRegistration gracefully instead of faulting the message.
        return channels.Find(evt.ChannelKind)?.CorrelationKey(evt) ?? evt.CorrelationKey ?? string.Empty;
    }
}

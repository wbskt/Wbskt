using MassTransit;
using Wbskt.Events.Abstractions;

namespace Wbskt.Management.Host.Handlers;

public class SignalRForwarderDefinition<TEvent> : ConsumerDefinition<SignalRForwardingHandler<TEvent>> 
    where TEvent : WorkspaceEvent
{
    public SignalRForwarderDefinition()
    {
        // Set the custom endpoint name with the signalr-forwarder prefix
        EndpointName = $"signalr-forwarder-{typeof(TEvent).Name.ToLower()}";
    }
}

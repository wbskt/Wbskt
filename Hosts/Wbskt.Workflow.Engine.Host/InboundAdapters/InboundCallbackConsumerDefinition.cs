using MassTransit;
using Wbskt.Events.Workflow;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

public sealed class InboundCallbackConsumerDefinition : ConsumerDefinition<InboundCallbackConsumer>
{
    public InboundCallbackConsumerDefinition()
    {
        // The shared, durable queue the management host sends to (see InboundCallbackQueue). Only the
        // leader starts its bus, so a callback is held there across a restart or failover.
        EndpointName = InboundCallbackQueue.Name;
    }

    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator, IConsumerConfigurator<InboundCallbackConsumer> consumerConfigurator, IRegistrationContext context)
    {
        // A callback that fails (SQL briefly unavailable) is retried in place, and safely, because a
        // redelivery is deduplicated. Past the last try it moves to the _error queue, not lost.
        endpointConfigurator.UseMessageRetry(r => r.Exponential(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
    }
}

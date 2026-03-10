using MassTransit;

namespace Wbskt.Management.Host.Handlers.Events;

public sealed class EventLoggerHandlerDefinition : ConsumerDefinition<EventLoggerHandler>
{
    public EventLoggerHandlerDefinition()
    {
        // Name the shared queue explicitly to enable Competing Consumers
        EndpointName = "event-audit-log";
    }

    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator, IConsumerConfigurator<EventLoggerHandler> consumerConfigurator, IRegistrationContext context)
    {
        // Configure retries for the audit log endpoint
        endpointConfigurator.UseMessageRetry(r => r.Interval(5, TimeSpan.FromSeconds(2)));
    }
}

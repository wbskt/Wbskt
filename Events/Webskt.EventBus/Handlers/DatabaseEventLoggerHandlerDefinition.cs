using MassTransit;

namespace Webskt.EventBus.Handlers;

public sealed class DatabaseEventLoggerHandlerDefinition : ConsumerDefinition<DatabaseEventLoggerHandler>
{
    public DatabaseEventLoggerHandlerDefinition()
    {
        // Name the shared queue explicitly to enable Competing Consumers
        EndpointName = "event-audit-log";
    }

    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator, IConsumerConfigurator<DatabaseEventLoggerHandler> consumerConfigurator, IRegistrationContext context)
    {
        // Optional: Configure retries or other endpoint-specific settings here
        endpointConfigurator.UseMessageRetry(r => r.Interval(5, TimeSpan.FromSeconds(2)));
    }
}

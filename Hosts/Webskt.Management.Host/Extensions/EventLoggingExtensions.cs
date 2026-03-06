using MassTransit;
using Webskt.EventBus.Abstractions;
using Webskt.Foundation.Abstraction;
using Webskt.Management.Host.Handlers.Events;
using Webskt.Management.Host.Services.Events;

namespace Webskt.Management.Host.Extensions;

public static class EventLoggingExtensions
{
    public static Action<IBusRegistrationConfigurator> AddDatabaseEventLogging(this IServiceCollection services)
    {
        services.AddSingleton<IEventRegistry, EventRegistry>();
        services.AddTransient<IStartupTask, EventRegistryInitializationTask>();

        // Return the action so it can be passed into AddRabbitMQEventBus
        return x =>
        {
            x.AddConsumer<DatabaseEventLoggerHandler, DatabaseEventLoggerHandlerDefinition>();
        };
    }
}

using MassTransit;
using Wbskt.EventBus.Abstractions;
using Wbskt.Foundation.Abstraction;
using Wbskt.Management.Host.Handlers.Events;
using Wbskt.Management.Host.Services.Events;

namespace Wbskt.Management.Host.Extensions;

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

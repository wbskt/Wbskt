using MassTransit;
using Webskt.Common.Abstraction.Interfaces;
using Webskt.EventBus.Abstractions;
using Webskt.Management.Host.Handlers.Events;
using Webskt.Management.Host.Services.Events;

namespace Webskt.Management.Host.Extensions;

public static class EventLoggingExtensions
{
    public static void AddDatabaseEventLogging(this IServiceCollection services)
    {
        services.AddSingleton<IEventRegistry, EventRegistry>();
        services.AddTransient<IStartupTask, EventRegistryInitializationTask>();

        // Manually add the consumer to the MassTransit configuration
        services.AddMassTransit(x =>
        {
            x.AddConsumer<DatabaseEventLoggerHandler, DatabaseEventLoggerHandlerDefinition>();
        });
    }
}

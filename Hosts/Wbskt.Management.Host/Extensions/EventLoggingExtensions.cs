using MassTransit;
using Wbskt.EventBus.Abstractions;
using Wbskt.Foundation.Abstraction;
using Wbskt.Management.Host.Handlers.Events;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Events;

namespace Wbskt.Management.Host.Extensions;

public static class EventLoggingExtensions
{
    public static Action<IBusRegistrationConfigurator> AddDatabaseEventLogging(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EventLoggingOptions>(configuration.GetSection(EventLoggingOptions.SectionName));

        services.AddSingleton<IEventRegistry, EventRegistry>();
        services.AddTransient<IStartupTask, EventRegistryInitializationTask>();

        services.AddSingleton<EventLogBuffer>();
        services.AddHostedService<DatabaseBatchFlusherService>();

        // Return the action so it can be passed into AddRabbitMQEventBus
        return x =>
        {
            x.AddConsumer<EventLoggerHandler, EventLoggerHandlerDefinition>();
        };
    }
}

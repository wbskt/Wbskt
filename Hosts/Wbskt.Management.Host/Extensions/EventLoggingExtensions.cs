using MassTransit;
using Wbskt.EventBus.Abstractions;
using Wbskt.Management.Host.Handlers.Events;
using Wbskt.Management.Host.Hosting;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Services;
using Wbskt.Management.Host.Services.Events;
using Wbskt.Primitives;

namespace Wbskt.Management.Host.Extensions;

public static class EventLoggingExtensions
{
    public static Action<IBusRegistrationConfigurator> AddDatabaseEventLogging(this IServiceCollection services, IConfiguration configuration, HostRole role)
    {
        services.Configure<EventLoggingOptions>(configuration.GetSection(EventLoggingOptions.SectionName));

        services.AddSingleton<IEventRegistry, EventRegistry>();
        services.AddTransient<IStartupTask, EventRegistryInitializationTask>();

        // A periodic job, so only on a full instance, like the host's other jobs.
        if (role == HostRole.All)
        {
            services.AddHostedService<EventLogRetentionService>();
        }

        // Return the action so it can be passed into AddRabbitMQEventBus
        return x =>
        {
            x.AddConsumer<EventLoggerHandler, EventLoggerHandlerDefinition>();
        };
    }
}

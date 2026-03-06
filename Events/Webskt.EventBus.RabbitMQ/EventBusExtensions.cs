using System.Reflection;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Webskt.Common.Abstraction.Interfaces;
using Webskt.EventBus.Abstractions;
using Webskt.EventBus.Handlers;

namespace Webskt.EventBus.RabbitMQ;

public static class EventBusExtensions
{
    public static void AddRabbitMQEventBus(
        this IServiceCollection services,
        IConfiguration configuration,
        bool enableDbLogging = false,
        Action<RabbitMQOptions>? configure = null)
    {
        services.AddEventBusCore();

        if (enableDbLogging)
        {
            services.AddSingleton<IEventRegistry, EventRegistry>();
            services.AddTransient<IStartupTask, EventRegistryInitializationTask>();
        }

        var options = new RabbitMQOptions();
        configuration.GetSection("RabbitMQ").Bind(options);
        configure?.Invoke(options);

        services.AddMassTransit(x =>
        {
            if (enableDbLogging)
            {
                x.AddConsumer<DatabaseEventLoggerHandler, DatabaseEventLoggerHandlerDefinition>();
            }

            // 1. Discover and Register Consumers from Host Assemblies
            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly != null)
            {
                var assemblies = entryAssembly.GetReferencedAssemblies()
                    .Where(a => a.Name != null && a.Name.StartsWith("Webskt"))
                    .Select(Assembly.Load)
                    .ToList();
                
                assemblies.Add(entryAssembly);

                // Filter out the global logger from dynamic discovery
                x.AddConsumers(type => type != typeof(DatabaseEventLoggerHandler), assemblies.ToArray());
            }

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(options.HostName, h =>
                {
                    h.Username(options.UserName);
                    h.Password(options.Password);
                });

                // 2. Configure Endpoints automatically
                cfg.ConfigureEndpoints(context);
            });
        });

        // 3. Register IEventBus Wrapper as Singleton
        services.AddSingleton<IEventBus, MassTransitEventBus>();
    }
}

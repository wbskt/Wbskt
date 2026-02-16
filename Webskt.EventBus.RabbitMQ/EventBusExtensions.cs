using System.Reflection;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Webskt.Common.Data;
using Webskt.EventBus.Abstractions;
using Webskt.EventBus.Handlers;

namespace Webskt.EventBus.RabbitMQ;

public static class EventBusExtensions
{
    public static void AddRabbitMQEventBus(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<RabbitMQOptions>? configure = null)
    {
        // 1. Persistence Implementation (From Common)
        services.AddWebsktDataServices();

        var options = new RabbitMQOptions();
        configuration.GetSection("RabbitMQ").Bind(options);
        configure?.Invoke(options);

        services.AddMassTransit(x =>
        {
            // 2. Register Global Logger Consumer
            x.AddConsumer<DatabaseEventLoggerHandler>();

            // 3. Discover and Register Consumers from Host Assemblies
            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly != null)
            {
                var assemblies = entryAssembly.GetReferencedAssemblies()
                    .Where(a => a.Name != null && a.Name.StartsWith("Webskt"))
                    .Select(Assembly.Load)
                    .ToList();
                
                assemblies.Add(entryAssembly);

                x.AddConsumers(assemblies.ToArray());
            }

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(options.HostName, h =>
                {
                    h.Username(options.UserName);
                    h.Password(options.Password);
                });

                // 4. Configure Shared Queue for Audit Log (Competing Consumer)
                cfg.ReceiveEndpoint("event-audit-log", e =>
                {
                    e.ConfigureConsumer<DatabaseEventLoggerHandler>(context);
                });

                // 5. Configure Broadcast Queues for everything else (Instance Specific)
                cfg.ConfigureEndpoints(context);
            });
        });

        // 6. Register IEventBus Wrapper
        services.AddScoped<IEventBus, MassTransitEventBus>();
    }
}

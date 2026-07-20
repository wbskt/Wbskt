using System.Reflection;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wbskt.EventBus.Abstractions;

namespace Wbskt.EventBus.RabbitMQ;

// Reused as the socket presence HostId so a connection's owning instance can be identified
// independently of the MassTransit queue naming.
public sealed record BusInstanceId(string Value);

public static class EventBusExtensions
{
    public static void AddRabbitMqEventBus(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureBus = null,
        Action<RabbitMQOptions>? configureOptions = null,
        Action<IBusRegistrationContext, IRabbitMqBusFactoryConfigurator>? configureEndpoints = null,
        bool perInstanceEndpoints = false)
        {

        var options = new RabbitMQOptions();
        configuration.GetSection("RabbitMQ").Bind(options);
        configureOptions?.Invoke(options);

        string? instanceId = null;
        if (perInstanceEndpoints)
        {
            instanceId = configuration["EventBus:InstanceId"] ?? Environment.MachineName;
            services.AddSingleton(new BusInstanceId(instanceId));
        }

        services.AddMassTransit(x =>
        {
            // 1. Allow the caller to add specific consumers/definitions
            configureBus?.Invoke(x);

            if (perInstanceEndpoints)
            {
                // Every instance gets its own auto-delete queue so ClientCommandEvent etc. fan out
                // to all socket hosts instead of round-robining across a shared competing queue.
                x.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(instanceId!, false));
                x.AddConfigureEndpointsCallback((_, endpointConfigurator) =>
                {
                    if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rabbitMqConfigurator)
                    {
                        rabbitMqConfigurator.Durable = false;
                        rabbitMqConfigurator.AutoDelete = true;
                    }
                });
            }

            // 2. Discover and Register Consumers from Host Assemblies
            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly != null)
            {
                var assemblies = entryAssembly.GetReferencedAssemblies()
                    .Where(a => a.Name != null && a.Name.StartsWith("Wbskt"))
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

                cfg.ConfigureEndpoints(context);
                configureEndpoints?.Invoke(context, cfg);
            });
        });

        services.AddSingleton<IEventBus, MassTransitEventBus>();
    }
}

using System.Reflection;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Webskt.EventBus.Abstractions;

namespace Webskt.EventBus.RabbitMQ;

public static class EventBusExtensions
{
    public static void AddRabbitMQEventBus(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IBusRegistrationConfigurator>? configureBus = null,
        Action<RabbitMQOptions>? configureOptions = null)
    {
        services.AddEventBusCore();

        var options = new RabbitMQOptions();
        configuration.GetSection("RabbitMQ").Bind(options);
        configureOptions?.Invoke(options);

        services.AddMassTransit(x =>
        {
            // 1. Allow the caller to add specific consumers/definitions
            configureBus?.Invoke(x);

            // 2. Discover and Register Consumers from Host Assemblies
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

                cfg.ConfigureEndpoints(context);
            });
        });

        services.AddSingleton<IEventBus, MassTransitEventBus>();
    }
}

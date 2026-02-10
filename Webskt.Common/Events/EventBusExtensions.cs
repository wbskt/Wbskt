using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Webskt.Common.Abstraction.Events;
using Webskt.Common.Infrastructure.RabbitMQ;

namespace Webskt.Common.Events;

public static class EventBusExtensions
{
    public static IServiceCollection AddRabbitMQEventBus(this IServiceCollection services, Action<RabbitMQOptions>? configure = null)
    {
        var options = new RabbitMQOptions();
        configure?.Invoke(options);

        services.Configure<RabbitMQOptions>(o =>
        {
            o.HostName = options.HostName;
            o.UserName = options.UserName;
            o.Password = options.Password;
            o.ExchangeName = options.ExchangeName;
            o.PrefetchCount = options.PrefetchCount;
        });

        services.TryAddSingleton<EventHandlerResolver>();
        services.AddSingleton<IEventBus, RabbitMQBus>();
        services.AddHostedService<RabbitMQListener>();

        return services;
    }

    public static void AddEventHandlers(this IServiceCollection services, Assembly assembly)
    {
        services.TryAddSingleton<EventHandlerResolver>();

        var concreteTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false });

        foreach (var type in concreteTypes)
        {
            var handlerInterfaces = type.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventHandler<>))
                .ToList();

            if (handlerInterfaces.Count == 0)
            {
                continue;
            }

            // Register metadata for each event type it handles
            foreach (var @interface in handlerInterfaces)
            {
                var eventType = @interface.GetGenericArguments()[0];
                EventHandlerResolver.Register(eventType, type);
            }

            // Register the concrete handler type so it can be resolved from a scope
            services.TryAddScoped(type);
        }
    }
}

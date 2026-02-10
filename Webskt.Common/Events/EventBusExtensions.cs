using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Webskt.Common.Abstraction.Events;
using Webskt.Common.Infrastructure.RabbitMQ;

namespace Webskt.Common.Events;

public static class EventBusExtensions
{
    public static void AddRabbitMQEventBus(this IServiceCollection services, IConfiguration configuration,
        Action<RabbitMQOptions>? configure = null)
    {
        // 1. Manually bind the options to avoid ambiguous Configure overloads
        services.Configure<RabbitMQOptions>(options =>
        {
            configuration.GetSection("RabbitMQ").Bind(options);
            configure?.Invoke(options);
        });

        services.TryAddSingleton<EventHandlerResolver>();
        services.AddSingleton<IEventBus, RabbitMQBus>();
        services.AddHostedService<RabbitMQListener>();
    }

    /// <summary>
    /// Automatically discovers and registers all Events and Handlers in all loaded assemblies
    /// that start with "Webskt".
    /// </summary>
    public static IServiceCollection AddWebsktEventHandlers(this IServiceCollection services)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.FullName != null && a.FullName.StartsWith("Webskt"));

        foreach (var assembly in assemblies)
        {
            services.AddEventHandlers(assembly);
        }

        return services;
    }

    public static void AddEventHandlers(this IServiceCollection services, Assembly assembly)
    {
        // 1. Discover and register all Event Types in this assembly
        var eventTypes = assembly.GetTypes()
            .Where(t => typeof(IEvent).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false });

        foreach (var eventType in eventTypes)
        {
            services.AddSingleton(new EventTypeDescriptor(eventType));
        }

        // 2. Discover and register all Handlers
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

            foreach (var @interface in handlerInterfaces)
            {
                var handledEventType = @interface.GetGenericArguments()[0];
                services.AddSingleton(new HandlerDescriptor(handledEventType, type));
            }

            services.TryAddScoped(type);
        }
    }
}

public record HandlerDescriptor(Type EventType, Type HandlerType);
public record EventTypeDescriptor(Type EventType);
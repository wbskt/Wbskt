using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Webskt.Common.Abstraction.Interfaces;
using Webskt.Common.Data;
using Webskt.EventBus.Abstractions;
using Webskt.EventBus.Implementations;
using Webskt.EventBus.Infrastructure;

namespace Webskt.EventBus.RabbitMQ;

public static class EventBusExtensions
{
    public static void AddRabbitMQEventBus(this IServiceCollection services, IConfiguration configuration,
        Action<RabbitMQOptions>? configure = null)
    {
        // 1. Core Event Bus Services
        services.AddEventBusCore();

        // 2. RabbitMQ Configuration
        services.Configure<RabbitMQOptions>(options =>
        {
            configuration.GetSection("RabbitMQ").Bind(options);
            configure?.Invoke(options);
        });

        // 3. Transport Implementation
        services.AddSingleton<IEventBus, RabbitMQBus>();
        services.AddHostedService<RabbitMQListener>();

        // 4. Persistence Implementation (From Common)
        services.AddWebsktDataServices();
        
        // 5. RabbitMQ Specific Startup Tasks
        services.AddTransient<IStartupTask, EventBusInitializationStartupTask>();
    }

    public static void AddWebsktEventHandlers(this IServiceCollection services)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.FullName != null && a.FullName.StartsWith("Webskt"));

        foreach (var assembly in assemblies)
        {
            services.AddEventHandlers(assembly);
        }
    }

    private static void AddEventHandlers(this IServiceCollection services, Assembly assembly)
    {
        var eventTypes = assembly.GetTypes()
            .Where(t => typeof(IEvent).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false });

        foreach (var eventType in eventTypes)
        {
            services.AddSingleton(new EventTypeDescriptor(eventType));
        }

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

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Webskt.Common.Abstraction.Interfaces;
using Webskt.EventBus.Abstractions;
using Webskt.EventBus.Handlers;
using Webskt.EventBus.Implementations;
using Webskt.EventBus.Infrastructure;

namespace Webskt.EventBus;

public static class EventBusServiceExtensions
{
    public static void AddEventBusCore(this IServiceCollection services)
    {
        services.TryAddSingleton<EventHandlerResolver>();
        services.AddSingleton<IEventRegistry, EventRegistry>();
        
        // Register Core Startup Tasks
        services.AddTransient<IStartupTask, EventRegistryStartupTask>();

        // Register Global Handlers
        services.AddScoped<IEventHandler<IEvent>, DatabaseEventLoggerHandler>();
        services.AddSingleton(new EventTypeDescriptor(typeof(IEvent)));
        services.AddSingleton(new HandlerDescriptor(typeof(IEvent), typeof(DatabaseEventLoggerHandler)));
    }

    public static IServiceCollection AddWebsktEventHandlers(this IServiceCollection services)
    {
        // 1. Get the entry assembly (e.g. Webskt.Auth.Host)
        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly == null) return services;

        // 2. Discover all referenced assemblies that start with "Webskt"
        var assemblies = entryAssembly.GetReferencedAssemblies()
            .Where(a => a.Name != null && a.Name.StartsWith("Webskt"))
            .Select(Assembly.Load)
            .ToList();

        // 3. Include the entry assembly and current assembly
        assemblies.Add(entryAssembly);
        assemblies.Add(typeof(EventBusServiceExtensions).Assembly);

        foreach (var assembly in assemblies.Distinct())
        {
            services.AddEventHandlers(assembly);
        }

        return services;
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

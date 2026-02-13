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
        // Note: The AddWebsktEventHandlers discovery logic will also find this, 
        // but explicit registration ensures it's always there.
        services.AddScoped<IEventHandler<IEvent>, DatabaseEventLoggerHandler>();
        services.AddSingleton(new EventTypeDescriptor(typeof(IEvent)));
        services.AddSingleton(new HandlerDescriptor(typeof(IEvent), typeof(DatabaseEventLoggerHandler)));
    }
}

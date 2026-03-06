using System.Reflection;
using Webskt.Common.Abstraction.Interfaces;
using Webskt.EventBus.Abstractions;

namespace Webskt.Management.Host.Services.Events;

public sealed class EventRegistryInitializationTask : IStartupTask
{
    private readonly IEventProvider _eventProvider;
    private readonly IEventRegistry _registry;
    private readonly ILogger<EventRegistryInitializationTask> _logger;

    public EventRegistryInitializationTask(
        IEventProvider eventProvider,
        IEventRegistry registry,
        ILogger<EventRegistryInitializationTask> logger)
    {
        _eventProvider = eventProvider;
        _registry = registry;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing Event Registry from database...");

        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.FullName?.StartsWith("Webskt") == true);

        var eventTypes = assemblies.SelectMany(a => 
        {
            try { return a.GetTypes(); }
            catch { return Type.EmptyTypes; }
        })
        .Where(t => t.IsClass && !t.IsAbstract && typeof(BaseEvent).IsAssignableFrom(t));

        foreach (var type in eventTypes)
        {
            var eventName = type.Name;
            var attribute = type.GetCustomAttribute<EventCriticalityAttribute>();
            var criticality = attribute?.Criticality ?? EventCriticality.Info;

            try
            {
                var id = await _eventProvider.GetOrInsertEventIdAsync(eventName, (short)criticality, cancellationToken);
                _registry.RegisterEvent(eventName, id);
                _logger.LogDebug("Registered event {EventName} with ID {EventId}", eventName, id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register event {EventName} during startup.", eventName);
            }
        }
    }
}

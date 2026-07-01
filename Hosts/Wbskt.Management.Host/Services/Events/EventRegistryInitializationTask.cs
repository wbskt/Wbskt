using System.IO;
using System.Reflection;
using Wbskt.EventBus.Abstractions;
using Wbskt.Management.Host.Providers;
using Wbskt.Primitives;

namespace Wbskt.Management.Host.Services.Events;

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

        // Ensure all Wbskt assemblies in the application folder are loaded into memory 
        // to bypass lazy loading when registering startup tasks.
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        if (Directory.Exists(baseDirectory))
        {
            foreach (var file in Directory.GetFiles(baseDirectory, "Wbskt.*.dll"))
            {
                try
                {
                    Assembly.LoadFrom(file);
                }
                catch
                {
                    // Suppress dll load warnings/exceptions
                }
            }
        }

        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.FullName?.StartsWith("Wbskt") == true);

        var eventTypes = assemblies.SelectMany(a => 
        {
            try { return a.GetTypes(); }
            catch { return Type.EmptyTypes; }
        })
        .Where(t => t.IsClass && !t.IsAbstract && typeof(BaseEvent).IsAssignableFrom(t))
        .ToList();

        const int maxRetries = 15;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                foreach (var type in eventTypes)
                {
                    var eventName = type.Name;
                    var attribute = type.GetCustomAttribute<EventCriticalityAttribute>();
                    var criticality = attribute?.Criticality ?? EventCriticality.Info;

                    var id = await _eventProvider.GetOrInsertEventIdAsync(eventName, (short)criticality, cancellationToken);
                    _registry.RegisterEvent(eventName, id);
                    _logger.LogDebug("Registered event {EventName} with ID {EventId}", eventName, id);
                }
                
                _logger.LogInformation("Event Registry successfully initialized.");
                return;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger.LogWarning(ex, "Failed to initialize event registry (attempt {Attempt}/{Max}). Retrying in 3 seconds...", attempt, maxRetries);
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize event registry after {Max} attempts.", maxRetries);
                throw;
            }
        }
    }
}

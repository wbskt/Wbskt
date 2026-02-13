using System.Collections.Concurrent;
using Webskt.Common.Abstraction.Interfaces;
using Webskt.EventBus.Abstractions;

namespace Webskt.EventBus.Implementations;

internal sealed class EventRegistry : IEventRegistry
{
    private readonly IEventProvider _eventProvider;
    private readonly ConcurrentDictionary<Type, int> _eventIdCache = new();

    public EventRegistry(IEventProvider eventProvider)
    {
        _eventProvider = eventProvider;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var eventTypes = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a => a.FullName?.StartsWith("Webskt") ?? false)
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(IEvent).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false });

        foreach (var eventType in eventTypes)
        {
            var eventName = eventType.Name;
            var id = await _eventProvider.GetOrInsertEventIdAsync(eventName, cancellationToken);
            _eventIdCache[eventType] = id;
        }
    }

    public int GetEventId(Type eventType)
    {
        if (_eventIdCache.TryGetValue(eventType, out var id))
        {
            return id;
        }

        throw new KeyNotFoundException($"Event type {eventType.Name} not found in registry. Ensure it was discovered during initialization.");
    }
}

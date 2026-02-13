using System.Collections.Concurrent;

namespace Webskt.Common.Events;

public sealed class EventHandlerResolver
{
    private readonly ConcurrentDictionary<Type, IReadOnlyCollection<Type>> _handlerCache = new();
    private readonly IReadOnlyDictionary<string, Type> _nameToTypeMap;
    private readonly IReadOnlyCollection<HandlerDescriptor> _descriptors;

    public EventHandlerResolver(
        IEnumerable<HandlerDescriptor> descriptors,
        IEnumerable<EventTypeDescriptor> eventTypes)
    {
        _descriptors = descriptors.ToList().AsReadOnly();

        // Build the O(1) Name->Type lookup map
        // We include both explicitly handled event types AND discovered event types
        // to ensure base-class handlers work even for events without a specific handler.
        var allKnownTypes = _descriptors.Select(d => d.EventType)
            .Concat(eventTypes.Select(e => e.EventType))
            .Distinct();

        _nameToTypeMap = allKnownTypes.ToDictionary(
            t => t.FullName ?? t.Name, 
            t => t);
    }

    /// <summary>
    /// Efficiently resolves a C# Type from its string name (FullName).
    /// </summary>
    public Type? GetEventType(string typeName)
    {
        return _nameToTypeMap.GetValueOrDefault(typeName);
    }

    /// <summary>
    /// Resolves all registered handler types for a specific runtime event type.
    /// Supports polymorphic dispatch (base classes and interfaces).
    /// </summary>
    public IReadOnlyCollection<Type> GetHandlerTypes(Type runtimeEventType)
    {
        return _handlerCache.GetOrAdd(runtimeEventType, ResolveHandlerTypes);
    }

    private IReadOnlyCollection<Type> ResolveHandlerTypes(Type runtimeEventType)
    {
        var handlers = new HashSet<Type>();

        foreach (var descriptor in _descriptors)
        {
            if (descriptor.EventType.IsAssignableFrom(runtimeEventType))
            {
                handlers.Add(descriptor.HandlerType);
            }
        }

        return handlers.ToList().AsReadOnly();
    }
}

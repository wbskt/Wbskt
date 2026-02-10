using System.Collections.Concurrent;
using Webskt.Common.Abstraction.Events;

namespace Webskt.Common.Events;

public sealed class EventHandlerResolver
{
    private readonly ConcurrentDictionary<Type, IReadOnlyCollection<Type>> _handlerCache = new();
    private readonly IReadOnlyDictionary<string, Type> _nameToTypeMap;
    private readonly IReadOnlyCollection<HandlerDescriptor> _descriptors;

    public EventHandlerResolver(IEnumerable<HandlerDescriptor> descriptors)
    {
        _descriptors = descriptors.ToList().AsReadOnly();
        
        // Pre-build the name cache for O(1) routing key resolution
        _nameToTypeMap = _descriptors
            .Select(d => d.EventType)
            .Distinct()
            .ToDictionary(t => t.FullName ?? t.Name, t => t);
    }

    public Type? GetEventType(string typeName)
    {
        return _nameToTypeMap.GetValueOrDefault(typeName);
    }

    public IReadOnlyCollection<Type> GetHandlerTypes(Type runtimeEventType)
    {
        return _handlerCache.GetOrAdd(runtimeEventType, ResolveHandlerTypes);
    }

    private IReadOnlyCollection<Type> ResolveHandlerTypes(Type runtimeEventType)
    {
        var handlers = new HashSet<Type>();

        // Polymorphic support: finds handlers where the registered type is assignable from the actual event
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
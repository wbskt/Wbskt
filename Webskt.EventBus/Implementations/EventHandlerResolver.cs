using System.Collections.Concurrent;

namespace Webskt.EventBus.Implementations;

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

        var allKnownTypes = _descriptors.Select(d => d.EventType)
            .Concat(eventTypes.Select(e => e.EventType))
            .Distinct();

        _nameToTypeMap = allKnownTypes.ToDictionary(
            t => t.FullName ?? t.Name, 
            t => t);
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

public record HandlerDescriptor(Type EventType, Type HandlerType);
public record EventTypeDescriptor(Type EventType);

using System.Collections.Concurrent;
using Webskt.Common.Abstraction.Events;

namespace Webskt.Common.Events;

public class EventHandlerResolver
{
    // Maps "Event Type" -> List of "Handler Service Types"
    private static readonly ConcurrentDictionary<Type, IReadOnlyCollection<Type>> Cache = new();

    // The raw registration data: "TEvent defined in interface" -> "Handler Implementation Type"
    // e.g. IEvent -> EventsDatabaseLogHandler
    private static readonly List<(Type EventType, Type HandlerType)> Registrations = [];

    public IReadOnlyCollection<Type> GetHandlerTypes(Type runtimeEventType)
    {
        return Cache.GetOrAdd(runtimeEventType, ResolveHandlerTypes);
    }

    internal static void Register(Type eventType, Type handlerType)
    {
        Registrations.Add((eventType, handlerType));
        Cache.Clear(); // Invalidate cache on new registrations (though this mostly happens at startup)
    }

    private IReadOnlyCollection<Type> ResolveHandlerTypes(Type runtimeEventType)
    {
        var handlers = new HashSet<Type>();

        // Find all registrations where the registered EventType is assignable from the RuntimeEventType
        // This covers:
        // 1. Exact match (Registered: UserCreated, Runtime: UserCreated)
        // 2. Inheritance (Registered: BaseEvent, Runtime: ChildEvent)
        // 3. Interfaces (Registered: IEvent, Runtime: UserCreated)
        foreach (var (registeredEventType, handlerType) in Registrations)
        {
            if (registeredEventType.IsAssignableFrom(runtimeEventType))
            {
                handlers.Add(handlerType);
            }
        }

        return handlers.ToList();
    }
}

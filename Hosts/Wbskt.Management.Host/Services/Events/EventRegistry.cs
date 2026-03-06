using System.Collections.Concurrent;
using Wbskt.EventBus.Abstractions;

namespace Wbskt.Management.Host.Services.Events;

public sealed class EventRegistry : IEventRegistry
{
    private readonly ConcurrentDictionary<string, int> _cache = new();

    public int GetEventId(string eventName)
    {
        return _cache.GetValueOrDefault(eventName, 0);
    }

    public void RegisterEvent(string eventName, int eventId)
    {
        _cache[eventName] = eventId;
    }
}

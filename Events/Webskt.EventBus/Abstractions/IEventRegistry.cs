namespace Webskt.EventBus.Abstractions;

public interface IEventRegistry
{
    /// <summary>
    /// Resolves an event name to its internal database ID.
    /// </summary>
    int GetEventId(string eventName);

    /// <summary>
    /// Adds or updates a mapping in the registry.
    /// </summary>
    void RegisterEvent(string eventName, int eventId);
}

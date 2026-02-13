namespace Webskt.EventBus.Abstractions;

public interface IEventRegistry
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    int GetEventId(Type eventType);
}

namespace Webskt.Common.Abstraction.Interfaces;

public interface IEventRegistry
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    int GetEventId(Type eventType);
}

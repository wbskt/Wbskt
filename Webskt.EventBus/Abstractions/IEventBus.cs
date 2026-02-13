namespace Webskt.EventBus.Abstractions;

public interface IEventBus
{
    Task InitializeAsync(CancellationToken ct = default);
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent;
}

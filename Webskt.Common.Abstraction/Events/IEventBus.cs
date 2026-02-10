namespace Webskt.Common.Abstraction.Events;

public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent;
}

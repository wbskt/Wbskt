namespace Wbskt.EventBus;

public interface IEventBus
{
    Task PublishAsync(IEvent @event, CancellationToken cancellationToken);
    void Subscribe<TEvent, TEventHandler>() where TEvent : IEvent where TEventHandler : IEventHandler<TEvent>;
}

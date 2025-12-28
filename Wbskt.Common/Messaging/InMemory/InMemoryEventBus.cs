using Wbskt.EventBus;

namespace Wbskt.Common.Messaging.InMemory;

public class InMemoryEventBus : IEventBus
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<Type, List<Type>> _subscriptions = new();

    public InMemoryEventBus(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task PublishAsync(IEvent @event, CancellationToken cancellationToken)
    {
        var eventType = @event.GetType();
        if (!_subscriptions.TryGetValue(eventType, out var handlers))
        {
            return;
        }

        foreach (var handlerType in handlers)
        {
            if (_serviceProvider.GetService(handlerType) is not IEventHandler<IEvent> handler)
            {
                continue;
            }

            await handler.HandleAsync(@event, cancellationToken);
        }
    }

    public void Subscribe<TEvent, TEventHandler>()
        where TEvent : IEvent
        where TEventHandler : IEventHandler<TEvent>
    {
        var eventType = typeof(TEvent);
        var handlerType = typeof(TEventHandler);

        if (!_subscriptions.TryGetValue(eventType, out var handlers))
        {
            handlers = [];
            _subscriptions[eventType] = handlers;
        }

        handlers.Add(handlerType);
    }
}

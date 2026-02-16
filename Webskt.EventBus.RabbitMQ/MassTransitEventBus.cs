using MassTransit;
using Webskt.EventBus.Abstractions;

namespace Webskt.EventBus.RabbitMQ;

internal sealed class MassTransitEventBus : IEventBus
{
    private readonly IBus _bus;

    public MassTransitEventBus(IBus bus)
    {
        _bus = bus;
    }

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
    {
        return _bus.Publish(@event, ct);
    }
}

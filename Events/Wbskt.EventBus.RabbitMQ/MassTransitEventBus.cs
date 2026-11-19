using MassTransit;
using Wbskt.EventBus.Abstractions;

namespace Wbskt.EventBus.RabbitMQ;

internal sealed class MassTransitEventBus : IEventBus
{
    private readonly IBus _bus;

    public MassTransitEventBus(IBus bus)
    {
        _bus = bus;
    }

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
    {
        // As object, so MassTransit routes on the event's runtime type: one published through a
        // BaseEvent or IEvent variable still reaches the consumers of its own type.
        return _bus.Publish((object)@event, ct);
    }
}

using MassTransit;
using Webskt.EventBus.Abstractions;

namespace Webskt.EventBus.RabbitMQ;

internal sealed class MassTransitEventBus : IEventBus
{
    private readonly IPublishEndpoint _publishEndpoint;

    public MassTransitEventBus(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
    {
        return _publishEndpoint.Publish(@event, ct);
    }
}

using MassTransit;
using Webskt.EventBus.Abstractions;
using Webskt.Events.Client;

namespace Webskt.Management.Host.Handlers;

internal sealed class ClientPongHandler : IConsumer<ClientPongEvent>
{
    private readonly IEventBus _eventBus;

    public ClientPongHandler(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public async Task Consume(ConsumeContext<ClientPongEvent> context)
    {
        var pong = context.Message;
        var roundTrip = (DateTime.UtcNow - pong.OriginalPingTime).TotalMilliseconds;

        await _eventBus.PublishAsync(new ClientLatencyMeasuredEvent(pong.ClientRefId, pong.WorkspaceId, roundTrip), context.CancellationToken);
    }
}

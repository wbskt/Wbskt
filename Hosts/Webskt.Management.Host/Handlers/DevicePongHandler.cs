using MassTransit;
using Webskt.EventBus.Abstractions;
using Webskt.Events.Socket;

namespace Webskt.Management.Host.Handlers;

internal sealed class DevicePongHandler : IConsumer<DevicePongEvent>
{
    private readonly IEventBus _eventBus;

    public DevicePongHandler(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public async Task Consume(ConsumeContext<DevicePongEvent> context)
    {
        var pong = context.Message;
        var roundTrip = (DateTime.UtcNow - pong.OriginalPingTime).TotalMilliseconds;

        await _eventBus.PublishAsync(new ClientLatencyMeasuredEvent(pong.ClientRefId, pong.WorkspaceId, roundTrip), context.CancellationToken);
    }
}

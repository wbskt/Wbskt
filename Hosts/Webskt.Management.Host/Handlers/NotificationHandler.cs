using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Webskt.Events.Socket;
using Webskt.Management.Host.Hubs;

namespace Webskt.Management.Host.Handlers;

internal sealed class NotificationHandler : IConsumer<ClientLatencyMeasuredEvent>
{
    private readonly IHubContext<NotificationHub, INotificationClient> _hubContext;

    public NotificationHandler(IHubContext<NotificationHub, INotificationClient> hubContext)
    {
        _hubContext = hubContext;
    }

    public async Task Consume(ConsumeContext<ClientLatencyMeasuredEvent> context)
    {
        var @event = context.Message;

        await _hubContext.Clients
            .Group(@event.WorkspaceId.ToString())
            .ReceiveLatencyMeasurement(@event.ClientRefId, @event.RoundTripMs);
    }
}

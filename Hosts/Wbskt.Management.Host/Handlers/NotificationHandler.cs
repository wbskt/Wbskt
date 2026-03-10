using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Wbskt.Events.Client;
using Wbskt.Management.Host.Hubs;

namespace Wbskt.Management.Host.Handlers;

public sealed class NotificationHandler : IConsumer<ClientLatencyMeasuredEvent>
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

using System.Reflection;
using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Wbskt.Common.Abstraction;
using Wbskt.Events.Abstractions;
using Wbskt.Management.Host.Hubs;

namespace Wbskt.Management.Host.Handlers;

public class SignalRForwardingHandler<TEvent> : IConsumer<TEvent> 
    where TEvent : WorkspaceEvent
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly SignalRNotifyAttribute _metadata;

    public SignalRForwardingHandler(IHubContext<NotificationHub> hubContext)
    {
        _hubContext = hubContext;
        _metadata = typeof(TEvent).GetCustomAttribute<SignalRNotifyAttribute>()!;
    }

    public async Task Consume(ConsumeContext<TEvent> context)
    {
        var message = context.Message;

        var json = JsonSerializer.Serialize(context.Message, context.Message.GetType());
        await _hubContext.Clients.Group($"ws:{message.WorkspaceId}").SendAsync(_metadata.ClientMethod, json);
    }
}

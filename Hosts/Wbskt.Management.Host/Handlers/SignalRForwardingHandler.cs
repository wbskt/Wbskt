using System.Reflection;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Wbskt.Events;
using Wbskt.Events.Abstractions;
using Wbskt.Management.Host.Hubs;

namespace Wbskt.Management.Host.Handlers;

public class SignalRForwardingHandler<TEvent> : IConsumer<TEvent> 
    where TEvent : class, IWorkspaceContext
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ClientFeedThrottle _throttle;
    private readonly SignalRNotifyAttribute _metadata;

    public SignalRForwardingHandler(IHubContext<NotificationHub> hubContext, ClientFeedThrottle throttle)
    {
        _hubContext = hubContext;
        _throttle = throttle;
        _metadata = typeof(TEvent).GetCustomAttribute<SignalRNotifyAttribute>()!;

        if (_metadata.Scope == NotifyScope.Client && !typeof(IClientContext).IsAssignableFrom(typeof(TEvent)))
        {
            throw new InvalidOperationException($"{typeof(TEvent).Name} is client-scoped but does not implement {nameof(IClientContext)}.");
        }
    }

    public async Task Consume(ConsumeContext<TEvent> context)
    {
        var message = context.Message;

        if (_metadata.Scope != NotifyScope.Client || message is not IClientContext client)
        {
            await _hubContext.Clients.Group(NotificationHub.GroupName(message.WorkspaceId)).SendAsync(_metadata.ClientMethod, message);
            return;
        }

        var watchers = _hubContext.Clients.Group(NotificationHub.ClientGroupName(client.ClientId));
        var allowed = _throttle.TryAcquire(client.ClientId, out var skipped);

        if (skipped > 0)
        {
            await watchers.SendAsync(NotificationHub.ClientFeedSkippedMethod, new ClientFeedSkipped(client.ClientRefId, skipped, DateTime.UtcNow));
        }

        if (allowed)
        {
            await watchers.SendAsync(_metadata.ClientMethod, message);
        }
    }
}

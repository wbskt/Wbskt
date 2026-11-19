using System.Reflection;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Wbskt.Events;
using Wbskt.Events.Abstractions;
using Wbskt.Management.Host.Hubs;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Handlers;

public class SignalRForwardingHandler<TEvent> : IConsumer<TEvent> 
    where TEvent : class, IWorkspaceContext
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ClientFeedThrottle _throttle;
    private readonly WorkspaceMessageCounter _messageCounter;
    private readonly ITenantWorkspaceProvider _tenantWorkspaces;
    private readonly SignalRNotifyAttribute _metadata;

    public SignalRForwardingHandler(IHubContext<NotificationHub> hubContext, ClientFeedThrottle throttle, WorkspaceMessageCounter messageCounter, ITenantWorkspaceProvider tenantWorkspaces)
    {
        _hubContext = hubContext;
        _throttle = throttle;
        _messageCounter = messageCounter;
        _tenantWorkspaces = tenantWorkspaces;
        _metadata = typeof(TEvent).GetCustomAttribute<SignalRNotifyAttribute>()!;

        if (_metadata.Scope == NotifyScope.Client && !typeof(IClientContext).IsAssignableFrom(typeof(TEvent)))
        {
            throw new InvalidOperationException($"{typeof(TEvent).Name} is client-scoped but does not implement {nameof(IClientContext)}.");
        }

        if (_metadata.Scope == NotifyScope.Tenant && !typeof(ITenantContext).IsAssignableFrom(typeof(TEvent)))
        {
            throw new InvalidOperationException($"{typeof(TEvent).Name} is tenant-scoped but does not implement {nameof(ITenantContext)}.");
        }
    }

    public async Task Consume(ConsumeContext<TEvent> context)
    {
        var message = context.Message;

        if (_metadata.Scope == NotifyScope.Tenant && message is ITenantContext tenant)
        {
            // The event's own workspace too, in case its account copy has not been written yet.
            var workspaceIds = (await _tenantWorkspaces.GetWorkspaceIdsAsync(tenant.TenantId, context.CancellationToken))
                .Append(message.WorkspaceId)
                .Distinct()
                .Select(NotificationHub.GroupName)
                .ToList();
            await _hubContext.Clients.Groups(workspaceIds).SendAsync(_metadata.ClientMethod, message);
            return;
        }

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

        // After the sends: a send that throws is retried by the bus, and counting first would count it twice.
        _messageCounter.Count(message);
    }
}

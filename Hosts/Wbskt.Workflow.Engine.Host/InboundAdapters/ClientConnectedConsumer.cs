using MassTransit;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

/// <summary>
/// Parks the online presence checks for a device that just connected. A connect is not dispatched
/// through the inbound hub: no trigger listens on it, and dispatching it only wrote an idempotency
/// claim per connect. Presence triggers fire from the parked checks instead. The connect is also
/// noted in <see cref="RecentDeviceActivity"/>, for a command sent before its presence is recorded.
/// </summary>
public sealed class ClientConnectedConsumer(ClientPresenceParker parker, RecentDeviceActivity recentActivity) : IConsumer<ClientConnectedEvent>
{
    public Task Consume(ConsumeContext<ClientConnectedEvent> context)
    {
        ClientConnectedEvent evt = context.Message;
        recentActivity.Saw(evt.ClientRefId);
        return parker.ParkAsync(evt.ClientRefId, evt.WorkspaceId, ClientPresenceState.Online, evt.CreatedAtUtc, context.CancellationToken);
    }
}

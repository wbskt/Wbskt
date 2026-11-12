using MassTransit;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

/// <summary>
/// Parks the online presence checks for a client that just connected. A connect is not dispatched
/// through the inbound hub: no trigger listens on it, and dispatching it only wrote an idempotency
/// claim per connect. Presence triggers fire from the parked checks instead. A restored connection
/// (<see cref="ClientConnectedEvent.IsRestored"/>) parks nothing: the client never went offline. The
/// sweep's offline check does not fire either: the client shows connected, so it waits and is given up.
/// </summary>
public sealed class ClientConnectedConsumer(ClientPresenceParker parker) : IConsumer<ClientConnectedEvent>
{
    public Task Consume(ConsumeContext<ClientConnectedEvent> context)
    {
        ClientConnectedEvent evt = context.Message;
        if (evt.IsRestored)
        {
            return Task.CompletedTask;
        }

        return parker.ParkAsync(evt.ClientRefId, evt.WorkspaceId, ClientPresenceState.Online, evt.CreatedAtUtc, context.CancellationToken);
    }
}

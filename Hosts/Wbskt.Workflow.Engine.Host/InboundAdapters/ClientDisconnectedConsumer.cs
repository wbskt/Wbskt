using MassTransit;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Enums;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

public sealed class ClientDisconnectedConsumer(ClientPresenceParker parker) : IConsumer<ClientDisconnectedEvent>
{
    public Task Consume(ConsumeContext<ClientDisconnectedEvent> context)
    {
        ClientDisconnectedEvent evt = context.Message;
        return parker.ParkAsync(evt.ClientRefId, evt.WorkspaceId, ClientPresenceState.Offline, evt.CreatedAtUtc, context.CancellationToken);
    }
}

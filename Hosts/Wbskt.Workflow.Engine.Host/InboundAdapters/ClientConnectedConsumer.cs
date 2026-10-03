using System.Text.Json;
using MassTransit;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Enums;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

public sealed class ClientConnectedConsumer(IInboundHub hub, ClientPresenceParker parker) : IConsumer<ClientConnectedEvent>
{
    public async Task Consume(ConsumeContext<ClientConnectedEvent> context)
    {
        ClientConnectedEvent evt = context.Message;
        InboundEvent inboundEvent = new(
            "client-connected",
            [$"client:{evt.ClientRefId}"],
            $"client-connected:{evt.ClientRefId}:{InboundMessageId.Stable(context)}",
            new Dictionary<string, JsonElement>
            {
                ["clientRefId"] = JsonSerializer.SerializeToElement(evt.ClientRefId),
                ["clientId"] = JsonSerializer.SerializeToElement(evt.ClientId),
                ["workspaceId"] = JsonSerializer.SerializeToElement(evt.WorkspaceId)
            },
            default);

        await hub.HandleAsync(inboundEvent, context.CancellationToken);
        await parker.ParkAsync(evt.ClientRefId, evt.WorkspaceId, ClientPresenceState.Online, evt.CreatedAtUtc, context.CancellationToken);
    }
}

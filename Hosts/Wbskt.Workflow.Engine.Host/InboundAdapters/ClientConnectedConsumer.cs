using System.Text.Json;
using MassTransit;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

public sealed class ClientConnectedConsumer(IInboundHub hub) : IConsumer<ClientConnectedEvent>
{
    public Task Consume(ConsumeContext<ClientConnectedEvent> context)
    {
        ClientConnectedEvent evt = context.Message;
        InboundEvent inboundEvent = new(
            "client-connected",
            $"client:{evt.ClientRefId}",
            $"client-connected:{evt.ClientRefId}:{Guid.NewGuid()}",
            new Dictionary<string, JsonElement>
            {
                ["clientRefId"] = JsonSerializer.SerializeToElement(evt.ClientRefId),
                ["clientId"] = JsonSerializer.SerializeToElement(evt.ClientId),
                ["workspaceId"] = JsonSerializer.SerializeToElement(evt.WorkspaceId)
            },
            default);

        return hub.HandleAsync(inboundEvent, context.CancellationToken);
    }
}

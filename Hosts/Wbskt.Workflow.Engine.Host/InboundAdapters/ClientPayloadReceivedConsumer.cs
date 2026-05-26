using System.Text.Json;
using MassTransit;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

public sealed class ClientPayloadReceivedConsumer(IInboundHub hub) : IConsumer<ClientPayloadReceivedEvent>
{
    public Task Consume(ConsumeContext<ClientPayloadReceivedEvent> context)
    {
        ClientPayloadReceivedEvent evt = context.Message;
        InboundEvent inboundEvent = new(
            "client-payload",
            $"client:{evt.ClientRefId}",
            $"client-payload:{evt.ClientRefId}:{Guid.NewGuid()}",
            new Dictionary<string, JsonElement>
            {
                ["clientRefId"] = JsonSerializer.SerializeToElement(evt.ClientRefId),
                ["messageType"] = JsonSerializer.SerializeToElement(evt.MessageType),
                ["payload"] = JsonSerializer.SerializeToElement(evt.Payload)
            },
            default);

        return hub.HandleAsync(inboundEvent, context.CancellationToken);
    }
}

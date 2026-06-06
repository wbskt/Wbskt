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
            "device",
            $"device:{evt.ClientRefId}:{evt.MessageType}",
            $"client-payload:{evt.ClientRefId}:{InboundMessageId.Stable(context)}",
            new Dictionary<string, JsonElement>
            {
                ["deviceSerial"] = JsonSerializer.SerializeToElement(evt.ClientRefId.ToString()),
                ["payloadType"] = JsonSerializer.SerializeToElement(evt.MessageType),
                ["clientRefId"] = JsonSerializer.SerializeToElement(evt.ClientRefId),
                ["clientId"] = JsonSerializer.SerializeToElement(evt.ClientId),
                ["workspaceId"] = JsonSerializer.SerializeToElement(evt.WorkspaceId),
                ["payload"] = JsonSerializer.SerializeToElement(evt.Payload)
            },
            default);

        return hub.HandleAsync(inboundEvent, context.CancellationToken);
    }
}

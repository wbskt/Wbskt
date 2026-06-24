using System.Text.Json;
using MassTransit;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

public sealed class ClientPayloadReceivedConsumer(IInboundHub hub) : IConsumer<ClientMessageReceivedEvent>
{
    public Task Consume(ConsumeContext<ClientMessageReceivedEvent> context)
    {
        ClientMessageReceivedEvent evt = context.Message;
        InboundEvent inboundEvent = new(
            "device",
            [$"device:{evt.ClientRefId}:{evt.Type}", $"device:{evt.ClientRefId}:*"],
            $"client-payload:{evt.ClientRefId}:{InboundMessageId.Stable(context)}",
            new Dictionary<string, JsonElement>
            {
                ["deviceSerial"] = JsonSerializer.SerializeToElement(evt.ClientRefId.ToString()),
                ["payloadType"] = JsonSerializer.SerializeToElement(evt.Type),
                ["clientRefId"] = JsonSerializer.SerializeToElement(evt.ClientRefId),
                ["clientId"] = JsonSerializer.SerializeToElement(evt.ClientId),
                ["workspaceId"] = JsonSerializer.SerializeToElement(evt.WorkspaceId),
                ["payload"] = GetPayloadElement(evt.Payload)
            },
            default);

        return hub.HandleAsync(inboundEvent, context.CancellationToken);
    }

    private static JsonElement GetPayloadElement(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(payload);
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(payload);
        }
    }
}

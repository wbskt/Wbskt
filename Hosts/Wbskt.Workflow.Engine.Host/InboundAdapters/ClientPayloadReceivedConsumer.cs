using System.Text.Json;
using MassTransit;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

public sealed class ClientPayloadReceivedConsumer(IInboundHub hub, ClientHoldRecorder holdRecorder) : IConsumer<ClientMessageReceivedEvent>
{
    public async Task Consume(ConsumeContext<ClientMessageReceivedEvent> context)
    {
        ClientMessageReceivedEvent evt = context.Message;
        InboundEvent inboundEvent = new(
            "client",
            [$"client:{evt.ClientRefId}:{evt.Type}", $"client:{evt.ClientRefId}:*"],
            $"client-message:{evt.ClientRefId}:{InboundMessageId.Stable(context)}",
            new Dictionary<string, JsonElement>
            {
                ["messageType"] = JsonSerializer.SerializeToElement(evt.Type),
                ["clientRefId"] = JsonSerializer.SerializeToElement(evt.ClientRefId),
                ["clientId"] = JsonSerializer.SerializeToElement(evt.ClientId),
                ["workspaceId"] = JsonSerializer.SerializeToElement(evt.WorkspaceId),
                ["payload"] = GetPayloadElement(evt.Payload)
            },
            default);

        await hub.HandleAsync(inboundEvent, context.CancellationToken);
        await holdRecorder.RecordAsync(inboundEvent, evt.ClientRefId, evt.Type, evt.WorkspaceId, evt.CreatedAtUtc, context.CancellationToken);
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

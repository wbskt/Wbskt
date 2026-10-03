using System.Text.Json;
using MassTransit;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

public sealed class ClientPayloadReceivedConsumer(IInboundHub hub, ClientHoldRecorder holdRecorder) : IConsumer<ClientMessageReceivedEvent>
{
    /// <summary>A message that reached the platform this long after the device sent it was buffered offline.</summary>
    public static readonly TimeSpan LateThreshold = TimeSpan.FromSeconds(30);

    public async Task Consume(ConsumeContext<ClientMessageReceivedEvent> context)
    {
        ClientMessageReceivedEvent evt = context.Message;
        DateTime receivedAt = DateTime.SpecifyKind(evt.CreatedAtUtc, DateTimeKind.Utc);
        DateTime sentAt = evt.SentAtUtc is { } deviceTime ? DateTime.SpecifyKind(deviceTime, DateTimeKind.Utc) : receivedAt;
        bool late = receivedAt - sentAt > LateThreshold;

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
                ["payload"] = GetPayloadElement(evt.Payload),
                ["sentAt"] = JsonSerializer.SerializeToElement(sentAt),
                ["receivedAt"] = JsonSerializer.SerializeToElement(receivedAt),
                ["late"] = JsonSerializer.SerializeToElement(late)
            },
            default);

        await hub.HandleAsync(inboundEvent, context.CancellationToken);
        await holdRecorder.RecordAsync(inboundEvent, evt.ClientRefId, evt.Type, evt.WorkspaceId, sentAt, context.CancellationToken);
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

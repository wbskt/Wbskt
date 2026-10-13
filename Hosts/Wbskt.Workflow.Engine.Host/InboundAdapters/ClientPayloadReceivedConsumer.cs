using System.Text.Json;
using MassTransit;
using Wbskt.Events.Client;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Channels;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

public sealed class ClientPayloadReceivedConsumer(IInboundHub hub, ClientHoldRecorder holdRecorder, RecentDeviceActivity recentActivity) : IConsumer<ClientMessageReceivedEvent>
{
    /// <summary>A message that reached the platform this long after the device sent it was buffered offline.</summary>
    public static readonly TimeSpan LateThreshold = TimeSpan.FromSeconds(30);

    public async Task Consume(ConsumeContext<ClientMessageReceivedEvent> context)
    {
        ClientMessageReceivedEvent evt = context.Message;
        // Before the dispatch, so a run this message starts finds the device marked as just heard from.
        recentActivity.Saw(evt.ClientRefId);
        DateTime receivedAt = DateTime.SpecifyKind(evt.CreatedAtUtc, DateTimeKind.Utc);
        DateTime sentAt = evt.SentAtUtc is { } deviceTime ? DateTime.SpecifyKind(deviceTime, DateTimeKind.Utc) : receivedAt;
        bool late = receivedAt - sentAt > LateThreshold;

        InboundEvent inboundEvent = ClientChannel.Event(
            new ClientMessage(evt.ClientRefId, evt.ClientId, evt.WorkspaceId, evt.Type, GetPayloadElement(evt.Payload), sentAt, receivedAt, late),
            $"client-message:{evt.ClientRefId}:{InboundMessageId.Stable(context)}");

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

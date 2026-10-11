using System.Text.Json;
using MassTransit;
using Wbskt.Events.Workflow;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Channels;

namespace Wbskt.Workflow.Engine.Host.InboundAdapters;

/// <summary>
/// Hands public webhook and wake callbacks, sent by the management host to
/// <see cref="InboundCallbackQueue"/>, to the hub. Nobody waits on the outcome: the caller was
/// answered 202 when the broker took the message, so it is only logged.
/// </summary>
/// <remarks>
/// A redelivery is harmless. A webhook's event id comes from its idempotency key or the bus message
/// id (<see cref="WebhookEventIds"/>), so the dispatcher's claim drops the second delivery; a wake
/// resumes a bookmark that the first delivery already consumed, so the second finds nothing.
/// </remarks>
public sealed class InboundCallbackConsumer(IInboundHub hub, ILogger<InboundCallbackConsumer>? logger = null)
    : IConsumer<InboundWebhook>, IConsumer<InboundWake>
{
    public async Task Consume(ConsumeContext<InboundWebhook> context)
    {
        InboundWebhook webhook = context.Message;
        InboundEvent inboundEvent = WebhookChannel.Event(
            webhook.WorkspaceRef,
            webhook.Path,
            ParseBody(webhook.Body),
            WebhookEventIds.For(webhook.WorkspaceRef, webhook.Path, webhook.IdempotencyKey, webhook.Secret, InboundMessageId.Stable(context)),
            webhook.Secret);

        TriggerDispatchResult result = await hub.HandleAsync(inboundEvent, context.CancellationToken);
        logger?.LogInformation("Webhook {Path} in workspace {WorkspaceRef}: {Outcome} across {Count} registration(s).",
            webhook.Path, webhook.WorkspaceRef, result.Outcome, result.Registrations.Count);
    }

    public async Task Consume(ConsumeContext<InboundWake> context)
    {
        InboundEvent inboundEvent = HttpWakeChannel.Event(context.Message.Token, ParseBody(context.Message.Body), InboundMessageId.Stable(context));

        TriggerDispatchResult result = await hub.HandleAsync(inboundEvent, context.CancellationToken);
        logger?.LogInformation("Http wake: {Outcome}.", result.Outcome);
    }

    // The management host bound the body as JSON, so it parses; a message that does not is not one
    // it sent, and failing it parks it on the error queue.
    private static JsonElement ParseBody(string body)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }
}

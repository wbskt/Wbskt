using System.Reflection;
using System.Text;
using System.Text.Json;
using MassTransit;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services;

namespace Wbskt.Management.Host.Handlers.Events;

/// <summary>
/// Writes every event on the bus to dbo.EventLogs, a batch at a time. The batch is inserted before
/// the consumer returns, so RabbitMQ only drops the messages once they are saved: a crash, a deploy
/// or a SQL outage leaves them on the queue to be redelivered rather than lost. A redelivered
/// message is not logged twice, because the insert skips a MessageId the table already holds.
/// </summary>
public sealed class EventLoggerHandler : IConsumer<Batch<IEvent>>
{
    private readonly IEventRegistry _registry;
    private readonly IEventProvider _eventProvider;
    private readonly ILogger<EventLoggerHandler> _logger;

    public EventLoggerHandler(
        IEventRegistry registry,
        IEventProvider eventProvider,
        ILogger<EventLoggerHandler> logger)
    {
        _registry = registry;
        _eventProvider = eventProvider;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<Batch<IEvent>> context)
    {
        var entries = new List<EventLogEntry>(context.Message.Length);
        foreach (ConsumeContext<IEvent> message in context.Message)
        {
            if (await BuildEntryAsync(message) is { } entry)
            {
                entries.Add(entry);
            }
        }

        if (entries.Count > 0)
        {
            // Throwing here fails the whole batch, so the retry policy and then the broker keep every
            // message in it until the insert succeeds.
            await _eventProvider.InsertBatchAsync(EventLogTable.Build(entries), context.CancellationToken);
        }
    }

    private async Task<EventLogEntry?> BuildEntryAsync(ConsumeContext<IEvent> context)
    {
        var @event = context.Message;
        var messageTypeUrn = context.SupportedMessageTypes.FirstOrDefault();
        var eventName = messageTypeUrn?.Split(':').Last().Split('.').Last() ?? "UnknownEvent";

        var eventId = _registry.GetEventId(eventName);

        if (eventId <= 0)
        {
            // Registered on demand for an event type the startup task did not know about. A database
            // failure here propagates, like the insert's, so the batch is retried rather than dropped.
            var attribute = @event.GetType().GetCustomAttribute<EventCriticalityAttribute>();
            var criticality = attribute?.Criticality ?? EventCriticality.Info;

            eventId = await _eventProvider.GetOrInsertEventIdAsync(eventName, (short)criticality, context.CancellationToken);
            _registry.RegisterEvent(eventName, eventId);
        }

        if (eventId <= 0)
        {
            _logger.LogWarning("Event {EventName} is not registered in the EventRegistry. Skipping DB log.", eventName);
            return null;
        }

        try
        {
            var bodyBytes = context.ReceiveContext.GetBody();
            using var doc = JsonDocument.Parse(bodyBytes);
            
            var eventData = doc.RootElement.TryGetProperty("message", out var messageNode) 
                ? messageNode.GetRawText() 
                : (doc.RootElement.TryGetProperty("Message", out messageNode) ? messageNode.GetRawText() : Encoding.UTF8.GetString(bodyBytes));

            JsonElement targetNode = doc.RootElement.TryGetProperty("message", out var msgNode) 
                ? msgNode 
                : (doc.RootElement.TryGetProperty("Message", out msgNode) ? msgNode : doc.RootElement);

            int? workspaceId = (@event as IWorkspaceContext)?.WorkspaceId ?? GetIntProperty(targetNode, "workspaceId");
            int? policyId = (@event as IPolicyContext)?.PolicyId ?? GetIntProperty(targetNode, "policyId");
            Guid? policyRefId = (@event as IPolicyContext)?.PolicyRefId ?? GetGuidProperty(targetNode, "policyRefId");
            int? clientId = (@event as IClientContext)?.ClientId ?? GetIntProperty(targetNode, "clientId");
            Guid? clientRefId = (@event as IClientContext)?.ClientRefId ?? GetGuidProperty(targetNode, "clientRefId");
            int? workflowId = (@event as IWorkflowContext)?.WorkflowId ?? GetIntProperty(targetNode, "workflowId");
            Guid? workflowRefId = (@event as IWorkflowContext)?.WorkflowRefId ?? GetGuidProperty(targetNode, "workflowRefId");
            // The user an event is about (a login) or, for an action taken through the API, who took it.
            // The message is consumed as IEvent, so on the bus these casts miss and the values come from
            // the JSON body, where an actor event names its user actorUserId/actorUserRefId.
            int? userId = (@event as IUserContext)?.UserId ?? (@event as IActorContext)?.ActorUserId
                ?? GetIntProperty(targetNode, "userId") ?? GetIntProperty(targetNode, "actorUserId");
            Guid? userRefId = (@event as IUserContext)?.UserRefId ?? (@event as IActorContext)?.ActorUserRefId
                ?? GetGuidProperty(targetNode, "userRefId") ?? GetGuidProperty(targetNode, "actorUserRefId");

            var entry = new EventLogEntry(
                eventId, 
                eventData, 
                @event.CreatedAtUtc, 
                WorkspaceId: workspaceId,
                PolicyId: policyId,
                PolicyRefId: policyRefId,
                ClientId: clientId,
                ClientRefId: clientRefId,
                WorkflowId: workflowId,
                WorkflowRefId: workflowRefId,
                UserId: userId,
                UserRefId: userRefId,
                MessageId: context.MessageId
            );

            return entry;
        }
        catch (Exception ex)
        {
            // A body that can't be read now never will be, so it is skipped rather than retried.
            _logger.LogWarning(ex, "Could not read event {EventName} (message {MessageId}) for the event log; skipping it.", eventName, context.MessageId);
            return null;
        }
    }

    private static int? GetIntProperty(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            string capitalized = char.ToUpperInvariant(name[0]) + name.Substring(1);
            if (element.TryGetProperty(name, out var prop) || element.TryGetProperty(capitalized, out prop))
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var val))
                {
                    return val;
                }
            }
        }
        return null;
    }

    private static Guid? GetGuidProperty(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            string capitalized = char.ToUpperInvariant(name[0]) + name.Substring(1);
            if (element.TryGetProperty(name, out var prop) || element.TryGetProperty(capitalized, out prop))
            {
                if (prop.ValueKind == JsonValueKind.String && prop.TryGetGuid(out var val))
                {
                    return val;
                }
            }
        }
        return null;
    }
}

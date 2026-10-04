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

public sealed class EventLoggerHandler : IConsumer<IEvent>
{
    private readonly EventLogBuffer _buffer;
    private readonly IEventRegistry _registry;
    private readonly IEventProvider _eventProvider;
    private readonly ILogger<EventLoggerHandler> _logger;

    public EventLoggerHandler(
        EventLogBuffer buffer,
        IEventRegistry registry,
        IEventProvider eventProvider,
        ILogger<EventLoggerHandler> logger)
    {
        _buffer = buffer;
        _registry = registry;
        _eventProvider = eventProvider;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IEvent> context)
    {
        try
        {
            var @event = context.Message;
            var messageTypeUrn = context.SupportedMessageTypes.FirstOrDefault();
            var eventName = messageTypeUrn?.Split(':').Last().Split('.').Last() ?? "UnknownEvent";

            var eventId = _registry.GetEventId(eventName);

            if (eventId <= 0)
            {
                // Fallback to dynamic registration on demand to handle startup timing race conditions
                try
                {
                    var attribute = @event.GetType().GetCustomAttribute<EventCriticalityAttribute>();
                    var criticality = attribute?.Criticality ?? EventCriticality.Info;

                    eventId = await _eventProvider.GetOrInsertEventIdAsync(eventName, (short)criticality, context.CancellationToken);
                    _registry.RegisterEvent(eventName, eventId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to dynamically register event {EventName} on demand.", eventName);
                }
            }

            if (eventId <= 0)
            {
                _logger.LogWarning("Event {EventName} is not registered in the EventRegistry. Skipping DB log.", eventName);
                return;
            }

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
            int? userId = (@event as IUserContext)?.UserId ?? (@event as IActorContext)?.ActorUserId ?? GetIntProperty(targetNode, "userId");
            Guid? userRefId = (@event as IUserContext)?.UserRefId ?? (@event as IActorContext)?.ActorUserRefId ?? GetGuidProperty(targetNode, "userRefId");

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
                UserRefId: userRefId
            );

            await _buffer.WriteAsync(entry, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write event to buffer.");
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

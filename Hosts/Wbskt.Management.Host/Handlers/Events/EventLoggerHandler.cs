using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
            entries.AddRange(await BuildEntriesAsync(message));
        }

        if (entries.Count > 0)
        {
            // Throwing here fails the whole batch, so the retry policy and then the broker keep every
            // message in it until the insert succeeds.
            await _eventProvider.InsertBatchAsync(EventLogTable.Build(entries), context.CancellationToken);
        }
    }

    /// <summary>
    /// The event's rows: one, or one per workspace for an event about a tenant's people or a person's
    /// own account (<see cref="IWorkspacesContext"/>), which belongs in several workspaces' logs.
    /// </summary>
    private async Task<IReadOnlyList<EventLogEntry>> BuildEntriesAsync(ConsumeContext<IEvent> context)
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
            return [];
        }

        try
        {
            var bodyBytes = context.ReceiveContext.GetBody();
            using var doc = JsonDocument.Parse(bodyBytes);
            
            bool enveloped = doc.RootElement.TryGetProperty("message", out var messageNode) || doc.RootElement.TryGetProperty("Message", out messageNode);
            JsonElement targetNode = enveloped ? messageNode : doc.RootElement;
            var eventData = enveloped ? messageNode.GetRawText() : Encoding.UTF8.GetString(bodyBytes);

            int? workspaceId = (@event as IWorkspaceContext)?.WorkspaceId ?? GetIntProperty(targetNode, "workspaceId");
            int? policyId = (@event as IPolicyContext)?.PolicyId ?? GetIntProperty(targetNode, "policyId");
            Guid? policyRefId = (@event as IPolicyContext)?.PolicyRefId ?? GetGuidProperty(targetNode, "policyRefId");
            int? clientId = (@event as IClientContext)?.ClientId ?? GetIntProperty(targetNode, "clientId");
            Guid? clientRefId = (@event as IClientContext)?.ClientRefId ?? GetGuidProperty(targetNode, "clientRefId");
            int? workflowId = (@event as IWorkflowContext)?.WorkflowId ?? GetIntProperty(targetNode, "workflowId");
            // A command a run sent is about the client, but the workflow is who sent it: log it under both.
            Guid? workflowRefId = (@event as IWorkflowContext)?.WorkflowRefId ?? GetGuidProperty(targetNode, "workflowRefId")
                ?? GetGuidProperty(targetNode, "actorWorkflowRefId");
            // The user an event is about (a login) or, for an action taken through the API, who took it.
            // The message is consumed as IEvent, so on the bus these casts miss and the values come from
            // the JSON body, where an actor event names its user actorUserId/actorUserRefId.
            int? userId = (@event as IUserContext)?.UserId ?? (@event as IActorContext)?.ActorUserId
                ?? GetIntProperty(targetNode, "userId") ?? GetIntProperty(targetNode, "actorUserId");
            Guid? userRefId = (@event as IUserContext)?.UserRefId ?? (@event as IActorContext)?.ActorUserRefId
                ?? GetGuidProperty(targetNode, "userRefId") ?? GetGuidProperty(targetNode, "actorUserRefId");

            // Where it came from, and the caller's address and user agent: the last two move to their
            // own columns and out of the stored event, so retention and privacy rules apply in one place.
            // A sign-in names its address ipAddress. The workspace list is how the row got here, and
            // would show one workspace's readers the ids of the tenant's others.
            var source = EventSources.Derive(eventName, GetSourceProperty(targetNode), userId is not null);
            var clientAddress = GetStringProperty(targetNode, "clientAddress") ?? GetStringProperty(targetNode, "ipAddress");
            var userAgent = GetStringProperty(targetNode, "userAgent");
            var workspaceIds = workspaceId is null ? GetIntArrayProperty(targetNode, "workspaceIds") : [];
            var moved = new[] { "clientAddress", "ipAddress", "userAgent", "workspaceIds" }.Where(n => TryGetProperty(targetNode, n, out _)).ToArray();
            if (moved.Length > 0)
            {
                eventData = Without(eventData, moved);
            }

            EventLogEntry Entry(int? workspace) => new(
                eventId, 
                eventData, 
                @event.CreatedAtUtc, 
                WorkspaceId: workspace,
                PolicyId: policyId,
                PolicyRefId: policyRefId,
                ClientId: clientId,
                ClientRefId: clientRefId,
                WorkflowId: workflowId,
                WorkflowRefId: workflowRefId,
                UserId: userId,
                UserRefId: userRefId,
                MessageId: context.MessageId,
                Source: source,
                ClientAddress: Truncate(clientAddress, 45),
                UserAgent: Truncate(userAgent, 256)
            );

            return workspaceIds.Count > 0 ? workspaceIds.Select(id => Entry(id)).ToList() : [Entry(workspaceId)];
        }
        catch (Exception ex)
        {
            // A body that can't be read now never will be, so it is skipped rather than retried.
            _logger.LogWarning(ex, "Could not read event {EventName} (message {MessageId}) for the event log; skipping it.", eventName, context.MessageId);
            return [];
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

    private static IReadOnlyList<int> GetIntArrayProperty(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var prop) || prop.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return prop.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out _))
            .Select(e => e.GetInt32())
            .Distinct()
            .ToList();
    }

    private static string? GetStringProperty(JsonElement element, string name)
    {
        return TryGetProperty(element, name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    /// <summary>The stamped source, written as its number or its name depending on the serializer.</summary>
    private static EventSource? GetSourceProperty(JsonElement element)
    {
        if (!TryGetProperty(element, "actorSource", out var prop))
        {
            return null;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.Number when prop.TryGetByte(out var number) && Enum.IsDefined((EventSource)number) => (EventSource)number,
            JsonValueKind.String when Enum.TryParse<EventSource>(prop.GetString(), ignoreCase: true, out var named) => named,
            _ => null
        };
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement prop)
    {
        prop = default;
        return element.ValueKind == JsonValueKind.Object
            && (element.TryGetProperty(name, out prop) || element.TryGetProperty(char.ToUpperInvariant(name[0]) + name[1..], out prop));
    }

    /// <summary>The event's JSON without the named properties, matched in either casing.</summary>
    internal static string Without(string json, params string[] names)
    {
        if (JsonNode.Parse(json) is not JsonObject body)
        {
            return json;
        }

        foreach (var key in body.Select(p => p.Key).Where(k => names.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            body.Remove(key);
        }

        return body.ToJsonString();
    }

    private static string? Truncate(string? value, int length) => value is { Length: var n } && n > length ? value[..length] : value;
}

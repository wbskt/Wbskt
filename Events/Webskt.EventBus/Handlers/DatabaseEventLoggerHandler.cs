using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MassTransit;
using Microsoft.Extensions.Logging;
using Webskt.Common.Abstraction.Interfaces;
using Webskt.EventBus.Abstractions;

namespace Webskt.EventBus.Handlers;

public sealed class DatabaseEventLoggerHandler : IConsumer<IEvent>
{
    private readonly IEventProvider _eventProvider;
    private readonly ILogger<DatabaseEventLoggerHandler> _logger;

    // Cache: EventName -> (EventId, Criticality)
    private static readonly ConcurrentDictionary<string, (int Id, EventCriticality Criticality)> EventMetadataCache = new();
    
    // Cache: EventName -> Concrete Type (for attribute extraction)
    private static readonly ConcurrentDictionary<string, Type> EventTypeMap = new();

    public DatabaseEventLoggerHandler(
        IEventProvider eventProvider,
        ILogger<DatabaseEventLoggerHandler> logger)
    {
        _eventProvider = eventProvider;
        _logger = logger;
        
        InitializeEventTypeMap();
    }

    public async Task Consume(ConsumeContext<IEvent> context)
    {
        try
        {
            var messageTypeUrn = context.SupportedMessageTypes.FirstOrDefault();
            var eventName = messageTypeUrn?.Split(':').Last().Split('.').Last() ?? "UnknownEvent";

            // 1. Resolve metadata (ID and Criticality)
            if (!EventMetadataCache.TryGetValue(eventName, out var metadata))
            {
                var criticality = GetEventCriticality(eventName);
                // TODO: there will be concurrency
                // int eventId;
                // lock (Locker)
                // {
                //     eventId = _eventProvider.GetOrInsertEventIdAsync(eventName, (short)criticality, context.CancellationToken).Result;
                // }
                var eventId = await _eventProvider.GetOrInsertEventIdAsync(eventName, (short)criticality, context.CancellationToken);
                
                metadata = (eventId, criticality);
                EventMetadataCache.TryAdd(eventName, metadata);
            }

            // 2. Extract event data
            var bodyBytes = context.ReceiveContext.GetBody();
            using var doc = JsonDocument.Parse(bodyBytes);
            
            var eventData = doc.RootElement.TryGetProperty("message", out var messageNode) 
                ? messageNode.GetRawText() 
                : Encoding.UTF8.GetString(bodyBytes);

            int? workspaceId = null;
            if (doc.RootElement.TryGetProperty("message", out var msg) && msg.TryGetProperty("workspaceId", out var wsId) && wsId.TryGetInt32(out var id))
            {
                workspaceId = id;
            }

            // 3. Log to database
            await _eventProvider.InsertEventLogAsync(metadata.Id, eventData, context.Message.CreatedAtUtc, workspaceId, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log event to database.");
        }
    }

    private EventCriticality GetEventCriticality(string eventName)
    {
        if (EventTypeMap.TryGetValue(eventName, out var type))
        {
            var attribute = type.GetCustomAttribute<EventCriticalityAttribute>();
            return attribute?.Criticality ?? EventCriticality.Info;
        }

        return EventCriticality.Info;
    }

    private static void InitializeEventTypeMap()
    {
        lock (EventTypeMap)
        {
            if (EventTypeMap.IsEmpty)
            {
                var eventTypes = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Webskt.Events").ExportedTypes
                    .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(BaseEvent).IsAssignableFrom(t));

                foreach (var type in eventTypes)
                {
                    EventTypeMap.TryAdd(type.Name, type);
                }
            }
        }
    }
}

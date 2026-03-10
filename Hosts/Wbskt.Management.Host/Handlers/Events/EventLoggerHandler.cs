using System.Text;
using System.Text.Json;
using MassTransit;
using Wbskt.Common.Models;
using Wbskt.EventBus.Abstractions;
using Wbskt.Management.Host.Services;

namespace Wbskt.Management.Host.Handlers.Events;

public sealed class EventLoggerHandler : IConsumer<IEvent>
{
    private readonly EventLogBuffer _buffer;
    private readonly IEventRegistry _registry;
    private readonly ILogger<EventLoggerHandler> _logger;

    public EventLoggerHandler(
        EventLogBuffer buffer,
        IEventRegistry registry,
        ILogger<EventLoggerHandler> logger)
    {
        _buffer = buffer;
        _registry = registry;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IEvent> context)
    {
        try
        {
            var messageTypeUrn = context.SupportedMessageTypes.FirstOrDefault();
            var eventName = messageTypeUrn?.Split(':').Last().Split('.').Last() ?? "UnknownEvent";

            var eventId = _registry.GetEventId(eventName);

            if (eventId <= 0)
            {
                _logger.LogWarning("Event {EventName} is not registered in the EventRegistry. Skipping DB log.", eventName);
                return;
            }

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

            var entry = new EventLogEntry(eventId, eventData, context.Message.CreatedAtUtc, workspaceId);
            await _buffer.WriteAsync(entry, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write event to buffer.");
        }
    }
}

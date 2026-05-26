using System.Text;
using System.Text.Json;
using MassTransit;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events.Abstractions;
using Wbskt.Management.Host.Models;
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
            var @event = context.Message;
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

            var entry = new EventLogEntry(
                eventId, 
                eventData, 
                @event.CreatedAtUtc, 
                WorkspaceId: (@event as IWorkspaceContext)?.WorkspaceId,
                PolicyId: (@event as IPolicyContext)?.PolicyId,
                PolicyRefId: (@event as IPolicyContext)?.PolicyRefId,
                ClientId: (@event as IClientContext)?.ClientId,
                ClientRefId: (@event as IClientContext)?.ClientRefId
            );

            await _buffer.WriteAsync(entry, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write event to buffer.");
        }
    }
}

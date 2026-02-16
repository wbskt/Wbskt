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

    public DatabaseEventLoggerHandler(
        IEventProvider eventProvider,
        ILogger<DatabaseEventLoggerHandler> logger)
    {
        _eventProvider = eventProvider;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<IEvent> context)
    {
        try
        {
            // 1. Get the actual concrete event name from MassTransit's metadata
            var messageTypeUrn = context.SupportedMessageTypes.FirstOrDefault();
            var eventName = messageTypeUrn?.Split(':').Last().Split('.').Last() ?? "UnknownEvent";

            // 2. Get the raw JSON body and extract ONLY the event payload
            var bodyBytes = context.ReceiveContext.GetBody();
            using var doc = JsonDocument.Parse(bodyBytes);
            
            // MassTransit wraps the event in a "message" property in its JSON envelope.
            // If it exists, we take just that part. Otherwise, we take the whole body as a fallback.
            var eventData = doc.RootElement.TryGetProperty("message", out var messageNode) 
                ? messageNode.GetRawText() 
                : Encoding.UTF8.GetString(bodyBytes);

            // 3. Persist to database
            var eventId = await _eventProvider.GetOrInsertEventIdAsync(eventName, context.CancellationToken);
            await _eventProvider.InsertEventLogAsync(eventId, eventData, context.Message.CreatedAtUtc, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log event to database.");
        }
    }
}
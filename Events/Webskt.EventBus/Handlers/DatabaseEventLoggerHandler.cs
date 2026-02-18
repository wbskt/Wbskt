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
            var messageTypeUrn = context.SupportedMessageTypes.FirstOrDefault();
            var eventName = messageTypeUrn?.Split(':').Last().Split('.').Last() ?? "UnknownEvent";

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

            var eventId = await _eventProvider.GetOrInsertEventIdAsync(eventName, context.CancellationToken);
            await _eventProvider.InsertEventLogAsync(eventId, eventData, context.Message.CreatedAtUtc, workspaceId, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log event to database.");
        }
    }
}
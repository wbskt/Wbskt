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
        var @event = context.Message;
        try
        {
            var eventType = @event.GetType();
            // We use the event's type name as the ID lookup was part of the old registry system.
            // With MassTransit, we can just log the type name directly or query the DB for the ID if needed.
            // For now, I'll rely on the existing GetOrInsertEventIdAsync logic in IEventProvider which is efficient.
            var eventId = await _eventProvider.GetOrInsertEventIdAsync(eventType.Name, context.CancellationToken);
            var eventData = JsonSerializer.Serialize(@event, eventType);

            await _eventProvider.InsertEventLogAsync(eventId, eventData, @event.CreatedAtUtc, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log event {@Event} to database.", @event);
        }
    }
}
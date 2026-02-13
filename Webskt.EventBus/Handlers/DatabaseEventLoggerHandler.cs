using System.Text.Json;
using Microsoft.Extensions.Logging;
using Webskt.Common.Abstraction.Interfaces;
using Webskt.EventBus.Abstractions;

namespace Webskt.EventBus.Handlers;

public sealed class DatabaseEventLoggerHandler : IEventHandler<IEvent>
{
    private readonly IEventProvider _eventProvider;
    private readonly IEventRegistry _eventRegistry;
    private readonly ILogger<DatabaseEventLoggerHandler> _logger;

    public DatabaseEventLoggerHandler(
        IEventProvider eventProvider,
        IEventRegistry eventRegistry,
        ILogger<DatabaseEventLoggerHandler> logger)
    {
        _eventProvider = eventProvider;
        _eventRegistry = eventRegistry;
        _logger = logger;
    }

    public async Task HandleAsync(IEvent @event, CancellationToken ct)
    {
        try
        {
            var eventType = @event.GetType();
            var eventId = _eventRegistry.GetEventId(eventType);
            var eventData = JsonSerializer.Serialize(@event, eventType);

            await _eventProvider.InsertEventLogAsync(eventId, eventData, @event.CreatedAtUtc, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log event {@Event} to database.", @event);
        }
    }
}

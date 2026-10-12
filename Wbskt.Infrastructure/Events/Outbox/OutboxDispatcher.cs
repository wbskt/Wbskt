using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wbskt.EventBus.Abstractions;

namespace Wbskt.Infrastructure.Events.Outbox;

/// <summary>
/// Publishes what the outbox holds, oldest first, and deletes each row once the broker has it.
/// </summary>
/// <remarks>
/// <para>
/// Delivery is at least once: a row whose publish succeeded but whose delete did not is sent again on
/// the next pass, so consumers of outbox events must tolerate a repeat (they already must, for broker
/// redelivery). A second replica of the same host would also send some rows twice, for the same reason
/// and with the same answer.
/// </para>
/// <para>
/// A failed publish ends the pass and the next one waits longer, up to <see cref="MaxBackoff"/>, so a
/// broker outage delays events without spinning; they stay in the table meanwhile. A row whose type
/// this host does not know (written by a newer version, then rolled back) is left in place and logged
/// once, rather than blocking the rows after it.
/// </para>
/// </remarks>
public sealed class OutboxDispatcher : BackgroundService
{
    internal const int BatchSize = 50;
    internal static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private static readonly MethodInfo PublishMethod = typeof(IEventBus).GetMethod(nameof(IEventBus.PublishAsync))!;
    private static readonly IReadOnlyDictionary<string, Type> KnownEventTypes = typeof(Wbskt.Events.Auth.WorkspaceDeletedEvent).Assembly
        .GetTypes()
        .Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false } && typeof(IEvent).IsAssignableFrom(t) && t.FullName is not null)
        .ToDictionary(t => t.FullName!, StringComparer.Ordinal);

    private readonly IOutboxStore _store;
    private readonly IEventBus _bus;
    private readonly ILogger<OutboxDispatcher> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly ConcurrentDictionary<long, byte> _unknownRows = new();
    private readonly ConcurrentDictionary<Type, MethodInfo> _publishers = new();

    public OutboxDispatcher(IOutboxStore store, IEventBus bus, ILogger<OutboxDispatcher> logger)
        : this(store, bus, logger, DefaultPollInterval)
    {
    }

    internal OutboxDispatcher(IOutboxStore store, IEventBus bus, ILogger<OutboxDispatcher> logger, TimeSpan pollInterval)
    {
        _store = store;
        _bus = bus;
        _logger = logger;
        _pollInterval = pollInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan wait = _pollInterval;
        while (!stoppingToken.IsCancellationRequested)
        {
            bool ok;
            try
            {
                ok = await DispatchPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The database itself is unreachable. Same answer as a broker outage: try again later.
                _logger.LogWarning(ex, "Could not read the event outbox; retrying.");
                ok = false;
            }

            wait = ok ? _pollInterval : TimeSpan.FromTicks(Math.Min(wait.Ticks * 2, MaxBackoff.Ticks));

            try
            {
                await Task.Delay(wait, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>One pass over the pending rows. False when a publish failed and the pass stopped early.</summary>
    internal async Task<bool> DispatchPendingAsync(CancellationToken ct)
    {
        IReadOnlyCollection<PendingOutboxMessage> pending;
        do
        {
            pending = await _store.GetPendingAsync(BatchSize, ct);
            int handled = 0;
            foreach (var row in pending)
            {
                if (_unknownRows.ContainsKey(row.Id))
                {
                    continue;
                }

                if (!TryRead(row, out var @event, out var type))
                {
                    _unknownRows.TryAdd(row.Id, 0);
                    continue;
                }

                try
                {
                    await PublishAsync(@event, type, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Could not publish outbox event {OutboxId} ({MessageType}); it stays queued.", row.Id, row.MessageType);
                    return false;
                }

                await _store.DeleteAsync(row.Id, ct);
                handled++;
            }

            // A full batch made of nothing but rows this host cannot read would loop forever.
            if (handled == 0)
            {
                return true;
            }
        }
        while (pending.Count == BatchSize);

        return true;
    }

    private bool TryRead(PendingOutboxMessage row, out object @event, out Type type)
    {
        @event = null!;
        if (!KnownEventTypes.TryGetValue(row.MessageType, out type!))
        {
            _logger.LogError("Outbox event {OutboxId} has a type this host does not know ({MessageType}); it is left in the outbox.", row.Id, row.MessageType);
            return false;
        }

        try
        {
            @event = JsonSerializer.Deserialize(row.Payload, type, OutboxMessage.JsonOptions)!;
            return @event is not null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Outbox event {OutboxId} ({MessageType}) could not be read; it is left in the outbox.", row.Id, row.MessageType);
            return false;
        }
    }

    private Task PublishAsync(object @event, Type type, CancellationToken ct)
    {
        // IEventBus routes on the static type (MassTransit binds exchanges to it), so the event is
        // published as its own type, not as IEvent.
        var publish = _publishers.GetOrAdd(type, t => PublishMethod.MakeGenericMethod(t));
        return (Task)publish.Invoke(_bus, [@event, ct])!;
    }
}

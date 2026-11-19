using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
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
/// this host does not know (written by a newer version, then rolled back) or cannot read is left in
/// place and logged once, rather than blocking the rows after it. Each batch is read after the last id
/// the pass saw, so however many such rows pile up at the head of the table, the rows behind them are
/// still sent. How many are left behind is the <c>wbskt_outbox_unreadable_rows</c> gauge, which alerts
/// as <c>OutboxRowsUnreadable</c>.
/// </para>
/// </remarks>
public sealed class OutboxDispatcher : BackgroundService
{
    /// <summary>The rows left in the outbox because this host cannot read them, for a host that exports this meter.</summary>
    public const string MeterName = "Wbskt.Events.Outbox";

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
    private readonly HashSet<long> _unknownRows = [];
    private readonly ConcurrentDictionary<Type, MethodInfo> _publishers = new();
    private readonly Meter _meter = new(MeterName);
    private int _unreadableRows;

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
        _meter.CreateObservableGauge("wbskt_outbox_unreadable_rows", () => Volatile.Read(ref _unreadableRows));
    }

    /// <summary>Rows the last complete pass left in the outbox because it could not read them.</summary>
    internal int UnreadableRows => Volatile.Read(ref _unreadableRows);

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
        long? afterId = null;
        var unreadable = new HashSet<long>();
        IReadOnlyCollection<PendingOutboxMessage> pending;
        do
        {
            pending = await _store.GetPendingAsync(BatchSize, afterId, ct);
            foreach (var row in pending)
            {
                afterId = row.Id;
                if (_unknownRows.Contains(row.Id))
                {
                    unreadable.Add(row.Id);
                    continue;
                }

                if (!TryRead(row, out var @event, out var type))
                {
                    _unknownRows.Add(row.Id);
                    unreadable.Add(row.Id);
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
            }
        }
        while (pending.Count == BatchSize);

        // Forget rows someone has since removed by hand, so the gauge falls back to zero.
        _unknownRows.IntersectWith(unreadable);
        Volatile.Write(ref _unreadableRows, unreadable.Count);
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

    public override void Dispose()
    {
        _meter.Dispose();
        base.Dispose();
    }
}

using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Wbskt.EventBus.Abstractions;

namespace Wbskt.Infrastructure.Events;

/// <summary>
/// An <see cref="IEventBus"/> that never waits for the broker. Publishing queues the event in process
/// and returns at once; <see cref="QueuedEventDispatcher"/> hands it to the real bus in the background.
/// </summary>
/// <remarks>
/// <para>
/// For events published after the work they describe has already been committed. Awaiting the broker
/// there makes RabbitMQ a dependency of the request: a slow or unreachable broker means a slow or
/// failed answer for a change that was in fact made. In the auth host that was a refresh that had
/// rotated the token answering 500; in the management host it was a rotated device secret replaced
/// by a 500, so the only copy of the new secret was lost, and a registration whose device retried
/// and created another client. The events record what happened; they are not part of deciding it.
/// </para>
/// <para>
/// Not for publishes that are the action itself (a device command, a ping): queuing those would
/// report success for something that may never happen. They keep the real bus.
/// </para>
/// <para>
/// Same shape as <c>OutboundMailQueue</c>: bounded, <see cref="ChannelWriter{T}.TryWrite"/> only, and
/// a full queue drops the event with a log line rather than blocking a caller. Queued events do not
/// survive a restart. Each entry captures the event's static type, because MassTransit routes on it;
/// queuing the bare <see cref="IEvent"/> would publish everything as that interface.
/// </para>
/// </remarks>
public sealed class QueuedEventBus : IEventBus, IDisposable
{
    /// <summary>The queue's depth and drops, for a host that exports this meter.</summary>
    public const string MeterName = "Wbskt.Events.Queue";

    internal const int Capacity = 10_000;

    private readonly Channel<QueuedEvent> _channel = Channel.CreateBounded<QueuedEvent>(
        new BoundedChannelOptions(Capacity)
        {
            // Wait, not a Drop* mode: under Drop*, TryWrite discards and still returns true, so a
            // dropped event could not be logged. Only WriteAsync would wait, and nothing calls it.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true
        });

    private readonly ILogger<QueuedEventBus> _logger;
    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _dropped;

    public QueuedEventBus(ILogger<QueuedEventBus> logger)
    {
        _logger = logger;
        // A depth that keeps climbing is the broker not keeping up, well before anything is dropped.
        _meter.CreateObservableGauge("wbskt_event_queue_depth", () => _channel.Reader.Count);
        _dropped = _meter.CreateCounter<long>("wbskt_event_queue_dropped_total");
    }

    /// <summary>Queues the event. Never blocks, never throws, and ignores <paramref name="ct"/>: the request may end before the event is sent.</summary>
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
    {
        Queue(new QueuedEvent(typeof(TEvent).Name, (bus, token) => bus.PublishAsync(@event, token)));
        return Task.CompletedTask;
    }

    /// <summary>Queues a send that does not go through the event bus (a command), to go out in order with the events.</summary>
    internal void Queue(string name, Func<CancellationToken, Task> send) => Queue(new QueuedEvent(name, (_, token) => send(token)));

    private void Queue(QueuedEvent queued)
    {
        if (!_channel.Writer.TryWrite(queued))
        {
            _logger.LogError("The event queue is full; {EventType} was dropped. The event bus is not keeping up.", queued.Name);
            _dropped.Add(1);
        }
    }

    internal IAsyncEnumerable<QueuedEvent> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);

    internal bool TryRead(out QueuedEvent? queued) => _channel.Reader.TryRead(out queued);

    public void Dispose() => _meter.Dispose();

    internal sealed record QueuedEvent(string Name, Func<IEventBus, CancellationToken, Task> Publish);
}

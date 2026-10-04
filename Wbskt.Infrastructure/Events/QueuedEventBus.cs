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
public sealed class QueuedEventBus : IEventBus
{
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

    public QueuedEventBus(ILogger<QueuedEventBus> logger)
    {
        _logger = logger;
    }

    /// <summary>Queues the event. Never blocks, never throws, and ignores <paramref name="ct"/>: the request may end before the event is sent.</summary>
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
    {
        var queued = new QueuedEvent(typeof(TEvent).Name, (bus, token) => bus.PublishAsync(@event, token));
        if (!_channel.Writer.TryWrite(queued))
        {
            _logger.LogError("The event queue is full; {EventType} was dropped. The event bus is not keeping up.", queued.Name);
        }

        return Task.CompletedTask;
    }

    internal IAsyncEnumerable<QueuedEvent> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);

    internal bool TryRead(out QueuedEvent? queued) => _channel.Reader.TryRead(out queued);

    internal sealed record QueuedEvent(string Name, Func<IEventBus, CancellationToken, Task> Publish);
}

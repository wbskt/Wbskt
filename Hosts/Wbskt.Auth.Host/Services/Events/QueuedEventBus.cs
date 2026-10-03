using System.Threading.Channels;
using Wbskt.EventBus.Abstractions;

namespace Wbskt.Auth.Host.Services.Events;

/// <summary>
/// An <see cref="IEventBus"/> that never waits for the broker. Publishing queues the event in process
/// and returns at once; <see cref="QueuedEventDispatcher"/> hands it to the real bus in the background.
/// </summary>
/// <remarks>
/// <para>
/// Sign-in, refresh and their failure paths publish audit events, and awaiting the broker there made
/// RabbitMQ a dependency of signing in: a slow or unreachable broker meant a slow or failed login,
/// and a refresh that had already rotated the token could still answer 500 because the event after
/// it did not go out. The events record what happened; they are not part of deciding it.
/// </para>
/// <para>
/// Same shape as <c>OutboundMailQueue</c>: bounded, <see cref="ChannelWriter{T}.TryWrite"/> only, and
/// a full queue drops the event with a log line rather than blocking a caller. Queued events do not
/// survive a restart. Each entry captures the event's static type, because MassTransit routes on it;
/// queuing the bare <see cref="IEvent"/> would publish everything as that interface.
/// </para>
/// </remarks>
internal sealed class QueuedEventBus : IEventBus
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
            _logger.LogError("The auth event queue is full; {EventType} was dropped. The event bus is not keeping up.", queued.Name);
        }

        return Task.CompletedTask;
    }

    internal IAsyncEnumerable<QueuedEvent> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);

    internal bool TryRead(out QueuedEvent? queued) => _channel.Reader.TryRead(out queued);

    internal sealed record QueuedEvent(string Name, Func<IEventBus, CancellationToken, Task> Publish);
}

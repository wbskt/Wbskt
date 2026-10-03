using Wbskt.EventBus.Abstractions;

namespace Wbskt.Auth.Host.Services.Events;

/// <summary>
/// Drains <see cref="QueuedEventBus"/> into the real event bus, off the request path.
/// </summary>
internal sealed class QueuedEventDispatcher : BackgroundService
{
    /// <summary>How long shutdown keeps sending what is still queued before giving up on it.</summary>
    internal static readonly TimeSpan DrainOnStop = TimeSpan.FromSeconds(5);

    private readonly QueuedEventBus _queue;
    private readonly IEventBus _bus;
    private readonly ILogger<QueuedEventDispatcher> _logger;

    public QueuedEventDispatcher(QueuedEventBus queue, IEventBus bus, ILogger<QueuedEventDispatcher> logger)
    {
        _queue = queue;
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var queued in _queue.ReadAllAsync(stoppingToken))
            {
                await PublishAsync(queued, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        // Whatever arrived while the host was stopping, sent on a short deadline of its own.
        using var drain = new CancellationTokenSource(DrainOnStop);
        while (!drain.IsCancellationRequested && _queue.TryRead(out var remaining) && remaining is not null)
        {
            await PublishAsync(remaining, drain.Token);
        }
    }

    private async Task PublishAsync(QueuedEventBus.QueuedEvent queued, CancellationToken ct)
    {
        try
        {
            await queued.Publish(_bus, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Shutdown interrupted publishing {EventType}; it was not sent.", queued.Name);
        }
        catch (Exception ex)
        {
            // One event lost, not the loop: the next one may well go through.
            _logger.LogError(ex, "Publishing {EventType} failed; it was not sent.", queued.Name);
        }
    }
}

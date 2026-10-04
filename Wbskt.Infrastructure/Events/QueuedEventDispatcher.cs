using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wbskt.EventBus.Abstractions;

namespace Wbskt.Infrastructure.Events;

/// <summary>
/// Drains <see cref="QueuedEventBus"/> into the real event bus, off the request path.
/// </summary>
/// <remarks>
/// A failed publish is retried with a growing delay before the dispatcher moves on, so a broker that
/// is restarting or briefly unreachable delays events rather than losing them. Retrying holds the
/// events behind it, which keeps them in order; the queue keeps accepting new ones meanwhile, up to
/// its capacity. An event that still fails after <see cref="RetryDelays"/> is given up on and logged,
/// so one that can never be sent does not stop everything after it.
/// </remarks>
public sealed class QueuedEventDispatcher : BackgroundService
{
    /// <summary>How long shutdown keeps sending what is still queued before giving up on it.</summary>
    internal static readonly TimeSpan DrainOnStop = TimeSpan.FromSeconds(5);

    /// <summary>The waits between attempts at one event: about four and a half minutes in all.</summary>
    internal static readonly TimeSpan[] DefaultRetryDelays =
    [
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)
    ];

    private readonly QueuedEventBus _queue;
    private readonly IEventBus _bus;
    private readonly ILogger<QueuedEventDispatcher> _logger;

    public QueuedEventDispatcher(QueuedEventBus queue, IEventBus bus, ILogger<QueuedEventDispatcher> logger)
        : this(queue, bus, logger, DefaultRetryDelays)
    {
    }

    internal QueuedEventDispatcher(QueuedEventBus queue, IEventBus bus, ILogger<QueuedEventDispatcher> logger, IReadOnlyList<TimeSpan> retryDelays)
    {
        _queue = queue;
        _bus = bus;
        _logger = logger;
        RetryDelays = retryDelays;
    }

    internal IReadOnlyList<TimeSpan> RetryDelays { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var queued in _queue.ReadAllAsync(stoppingToken))
            {
                await PublishWithRetryAsync(queued, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        // Whatever arrived while the host was stopping, sent on a short deadline of its own and
        // without retries: there is no time left to wait for a broker to come back.
        using var drain = new CancellationTokenSource(DrainOnStop);
        while (!drain.IsCancellationRequested && _queue.TryRead(out var remaining) && remaining is not null)
        {
            await TryPublishAsync(remaining, drain.Token);
        }
    }

    private async Task PublishWithRetryAsync(QueuedEventBus.QueuedEvent queued, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await queued.Publish(_bus, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogWarning("Shutdown interrupted publishing {EventType}; it was not sent.", queued.Name);
                return;
            }
            catch (Exception ex) when (attempt < RetryDelays.Count)
            {
                _logger.LogWarning("Publishing {EventType} failed (attempt {Attempt}); retrying in {Delay}. {Message}", queued.Name, attempt + 1, RetryDelays[attempt], ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Publishing {EventType} failed after {Attempts} attempts; it was not sent.", queued.Name, attempt + 1);
                return;
            }

            try
            {
                await Task.Delay(RetryDelays[attempt], ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Shutdown interrupted retrying {EventType}; it was not sent.", queued.Name);
                return;
            }
        }
    }

    private async Task TryPublishAsync(QueuedEventBus.QueuedEvent queued, CancellationToken ct)
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
            _logger.LogError(ex, "Publishing {EventType} failed; it was not sent.", queued.Name);
        }
    }
}

public static class QueuedEventBusExtensions
{
    /// <summary>
    /// Registers <see cref="QueuedEventBus"/>. Services opt in one by one, by being constructed with
    /// the queued bus in place of <see cref="IEventBus"/>; see
    /// <see cref="AddScopedWithQueuedEvents{TService,TImplementation}"/>.
    /// </summary>
    /// <remarks>
    /// The host also registers <see cref="QueuedEventDispatcher"/> as a hosted service, and does it
    /// after the event bus: hosted services stop in reverse order, so the dispatcher then stops while
    /// the bus is still up and can send what is left in the queue.
    /// </remarks>
    public static IServiceCollection AddQueuedEventBus(this IServiceCollection services)
    {
        services.AddSingleton<QueuedEventBus>();
        return services;
    }

    /// <summary>
    /// Registers a scoped service whose <see cref="IEventBus"/> constructor argument is the
    /// <see cref="QueuedEventBus"/>, so its publishes never wait on the broker.
    /// </summary>
    public static IServiceCollection AddScopedWithQueuedEvents<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services.AddScoped<TService>(sp => ActivatorUtilities.CreateInstance<TImplementation>(sp, (IEventBus)sp.GetRequiredService<QueuedEventBus>()));
        return services;
    }
}

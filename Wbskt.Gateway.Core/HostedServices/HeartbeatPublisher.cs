using MassTransit;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Events.Hosting;
using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Gateway.Core.HostedServices;

// Tells the management host this instance is alive, on a fixed interval. Presence is a lease on it:
// the clients this host holds stay "online" only while the beats keep arriving (see
// StalePresenceSweeper in the management host). The first beat goes out at once. Every few minutes it
// also sends the clients it holds, so the management host can clear presence whose disconnect was lost,
// and once more as soon as a beat gets through after one that did not, so clients expired while the
// broker was out of reach are set back online, and again shortly after that for the ones whose expiry
// was too recent for the first to repair.
internal sealed class HeartbeatPublisher : BackgroundService
{
    // A broker under a memory or disk alarm blocks a publish rather than failing it; past this the beat
    // counts as failed.
    internal static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(10);

    // The management host leaves a presence change younger than a minute to its own event; a second
    // snapshot past that repairs a client expired just before the broker came back.
    internal static readonly TimeSpan RecoveryFollowUp = TimeSpan.FromMinutes(2);

    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<HeartbeatPublisher> _logger;
    private readonly string _hostId;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _snapshotInterval;
    private readonly IConnectionManager _connections;
    private readonly TimeProvider _time;

    public HeartbeatPublisher(IBus publishEndpoint, BusInstanceId busInstanceId, IConfiguration configuration, IConnectionManager connections,
        TimeProvider time, ILogger<HeartbeatPublisher> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
        _connections = connections;
        _time = time;
        _hostId = busInstanceId.Value;
        _interval = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("Presence:HeartbeatIntervalSeconds", 30)));
        _snapshotInterval = TimeSpan.FromSeconds(Math.Max(60, configuration.GetValue("Presence:SnapshotIntervalSeconds", 300)));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        // The first snapshot waits one interval: connections are still arriving after a start.
        var nextSnapshot = _time.GetUtcNow() + _snapshotInterval;
        bool beatFailed = false;
        try
        {
            do
            {
                if (_time.GetUtcNow() >= nextSnapshot)
                {
                    nextSnapshot = _time.GetUtcNow() + _snapshotInterval;
                    await PublishSnapshotAsync(stoppingToken);
                }

                try
                {
                    await PublishBoundedAsync(new SocketHostHeartbeat(_hostId), stoppingToken);

                    // Beats that could not get through may have let the management host expire this host's
                    // clients; a snapshot at once sets the ones still held back online. Until one gets
                    // through, the next beat tries again.
                    if (beatFailed && await PublishSnapshotAsync(stoppingToken))
                    {
                        beatFailed = false;
                        nextSnapshot = _time.GetUtcNow() + RecoveryFollowUp;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    // The next beat retries; a few missed ones are within the management host's margin.
                    beatFailed = true;
                    _logger.LogWarning("Could not publish the socket host heartbeat: {Message}", ex.Message);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task<bool> PublishSnapshotAsync(CancellationToken ct)
    {
        var takenAt = _time.GetUtcNow().UtcDateTime;
        var held = _connections.GetConnections().Select(c => c.Key).ToList();
        try
        {
            await PublishBoundedAsync(new SocketHostPresenceSnapshot(_hostId, held, takenAt), ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Could not publish the socket host presence snapshot: {Message}", ex.Message);
            return false;
        }
    }

    private async Task PublishBoundedAsync<T>(T message, CancellationToken ct) where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(PublishTimeout);
        await _publishEndpoint.Publish(message, timeout.Token);
    }
}

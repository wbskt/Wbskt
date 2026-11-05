using MassTransit;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Events.Hosting;
using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Gateway.Core.HostedServices;

// Tells the management host this instance is alive, on a fixed interval. Presence is a lease on it:
// the clients this host holds stay "online" only while the beats keep arriving (see
// StalePresenceSweeper in the management host). The first beat goes out at once. Every few minutes it
// also sends the clients it holds, so the management host can clear presence whose disconnect was lost.
internal sealed class HeartbeatPublisher : BackgroundService
{
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
                    await _publishEndpoint.Publish(new SocketHostHeartbeat(_hostId), stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The next beat retries; a few missed ones are within the management host's margin.
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

    private async Task PublishSnapshotAsync(CancellationToken ct)
    {
        var takenAt = _time.GetUtcNow().UtcDateTime;
        var held = _connections.GetConnections().Select(c => c.Key).ToList();
        try
        {
            await _publishEndpoint.Publish(new SocketHostPresenceSnapshot(_hostId, held, takenAt), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Could not publish the socket host presence snapshot: {Message}", ex.Message);
        }
    }
}

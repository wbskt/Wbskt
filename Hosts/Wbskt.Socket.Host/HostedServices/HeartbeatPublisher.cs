using MassTransit;
using Wbskt.EventBus.RabbitMQ;
using Wbskt.Events.Hosting;

namespace Wbskt.Socket.Host.HostedServices;

// Tells the management host this instance is alive, on a fixed interval. Presence is a lease on it:
// the clients this host holds stay "online" only while the beats keep arriving (see
// StalePresenceSweeper in the management host). The first beat goes out at once.
internal sealed class HeartbeatPublisher : BackgroundService
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<HeartbeatPublisher> _logger;
    private readonly string _hostId;
    private readonly TimeSpan _interval;

    public HeartbeatPublisher(IBus publishEndpoint, BusInstanceId busInstanceId, IConfiguration configuration, ILogger<HeartbeatPublisher> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
        _hostId = busInstanceId.Value;
        _interval = TimeSpan.FromSeconds(Math.Max(1, configuration.GetValue("Presence:HeartbeatIntervalSeconds", 30)));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            do
            {
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
}

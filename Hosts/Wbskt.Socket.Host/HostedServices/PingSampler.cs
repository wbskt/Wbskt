using System.Net.WebSockets;
using Wbskt.Client.Sdk.Models;
using Wbskt.Socket.Host.Infrastructure;

namespace Wbskt.Socket.Host.HostedServices;

// Periodically pings every connected client so the console's latency readings stay fresh
// without any UI action. On-demand pings (the console's Ping button) go through the bus
// via ClientPingHandler; this sampler sends directly since it already runs in-process.
internal sealed class PingSampler : BackgroundService
{
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger<PingSampler> _logger;
    private readonly TimeSpan _interval;

    public PingSampler(IConnectionManager connectionManager, IConfiguration configuration, ILogger<PingSampler> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
        _interval = TimeSpan.FromSeconds(configuration.GetValue("PingSampler:IntervalSeconds", 60));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                foreach (var clientRefId in _connectionManager.GetConnectedClients())
                {
                    var connection = _connectionManager.GetConnection(clientRefId);
                    if (connection?.Socket.State != WebSocketState.Open)
                    {
                        continue;
                    }

                    try
                    {
                        var sentAt = DateTime.UtcNow;
                        connection.LastPingSentAt = sentAt;
                        await connection.SendAsync(new SocketMessage("sys.ping", new { timestamp = sentAt }), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug("Sampler ping to client {ClientRefId} failed: {Message}", clientRefId, ex.Message);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down.
        }
    }
}

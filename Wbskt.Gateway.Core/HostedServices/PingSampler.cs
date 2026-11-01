using Microsoft.Extensions.Options;
using Wbskt.Gateway.Core.Infrastructure;

namespace Wbskt.Gateway.Core.HostedServices;

// Pings every connected client once per PingSamplerOptions.Interval (PingSampler:Interval, five
// minutes by default) so the console's latency readings never go entirely stale. It runs whether or
// not anyone is looking, so the interval is long; on-demand pings (the console's Ping button) go
// through the bus via ClientPingHandler. This sampler sends directly since it already runs in-process.
internal sealed class PingSampler : BackgroundService
{
    private readonly IConnectionManager _connectionManager;
    private readonly ILogger<PingSampler> _logger;
    private readonly TimeSpan _interval;

    public PingSampler(IConnectionManager connectionManager, IOptions<PingSamplerOptions> options, ILogger<PingSampler> logger)
    {
        _connectionManager = connectionManager;
        _logger = logger;
        var interval = options.Value.Interval;
        _interval = interval > TimeSpan.Zero ? interval : PingSamplerOptions.DefaultInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                foreach (var (clientRefId, connection) in _connectionManager.GetConnections())
                {
                    if (!connection.IsOpen)
                    {
                        continue;
                    }

                    try
                    {
                        await connection.SendPingAsync(stoppingToken);
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

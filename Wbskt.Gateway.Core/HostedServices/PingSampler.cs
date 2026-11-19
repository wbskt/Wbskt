using Microsoft.Extensions.Options;
using Wbskt.Gateway.Core.Infrastructure;
using Wbskt.Infrastructure.Hosting;

namespace Wbskt.Gateway.Core.HostedServices;

/// <summary>
/// Pings every connected client once per <see cref="PingSamplerOptions.Interval"/> (PingSampler:Interval,
/// five minutes by default) so the console's latency readings never go entirely stale. It runs whether or
/// not anyone is looking, so the interval is long; on-demand pings (the console's Ping button) go
/// through the bus via ClientPingHandler. This sampler sends directly since it already runs in-process.
/// A few connections are pinged at a time, and each send is bounded by the connection itself, so a client
/// that stops reading costs its own connection and never holds up the round.
/// </summary>
internal sealed class PingSampler(
    IConnectionManager connectionManager,
    IOptions<PingSamplerOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<PingSampler> logger,
    TimeProvider clock)
    : PeriodicWorker(AlwaysOpenWorkGate.Instance, scopeFactory, logger, clock)
{
    internal const int Parallelism = 16;

    protected override string Name => "Ping sampler";

    protected override TimeSpan Interval { get; } =
        options.Value.Interval > TimeSpan.Zero ? options.Value.Interval : PingSamplerOptions.DefaultInterval;

    // Nothing is connected yet when the host starts.
    protected override bool RunOnStart => false;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var open = connectionManager.GetConnections().Where(c => c.Value.IsOpen).ToList();
        var failed = 0;

        await Parallel.ForEachAsync(open, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = ct }, async (entry, token) =>
        {
            try
            {
                await entry.Value.SendPingAsync(token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
            {
                Interlocked.Increment(ref failed);
                Logger.LogDebug("Sampler ping to client {ClientRefId} failed: {Message}", entry.Key, ex.Message);
            }
        });

        if (failed > 0)
        {
            Logger.LogInformation("Sampler pings failed for {Failed} of {Total} connections.", failed, open.Count);
        }
    }
}

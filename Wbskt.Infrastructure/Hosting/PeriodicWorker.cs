using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wbskt.Infrastructure.Telemetry;

namespace Wbskt.Infrastructure.Hosting;

/// <summary>
/// A job a host runs on a fixed interval: once at start, then every <see cref="Interval"/>.
/// The loop, the gate check, the per-tick scope, the fault guard and the tick metric live here;
/// a worker supplies only what one tick does (<see cref="RunOnceAsync"/>).
/// </summary>
/// <param name="gate">Asked before every tick; a closed gate skips the tick (the engine's standby).</param>
/// <param name="timeProvider">The clock the interval is measured on; the system clock when omitted.</param>
public abstract class PeriodicWorker(IWorkGate gate, IServiceScopeFactory scopeFactory, ILogger logger, TimeProvider? timeProvider = null) : BackgroundService
{
    protected ILogger Logger { get; } = logger;

    /// <summary>The worker's name in logs and the <c>worker</c> tag of the tick metric.</summary>
    protected abstract string Name { get; }

    protected abstract TimeSpan Interval { get; }

    /// <summary>One tick's work, given the services of a scope opened for it.</summary>
    protected abstract Task RunOnceAsync(IServiceProvider services, CancellationToken ct);

    /// <summary>
    /// Where the tick is counted: the host's <see cref="IWorkerTickMetrics"/>, or nowhere when it
    /// registers none.
    /// </summary>
    protected virtual IWorkerTickMetrics? FindTickMetrics(IServiceProvider services) => services.GetService<IWorkerTickMetrics>();

    /// <summary>
    /// Runs one tick: nothing while the gate is closed, otherwise <see cref="RunOnceAsync"/> in a fresh scope.
    /// A failure propagates, so a test sees it; the loop logs it and carries on.
    /// </summary>
    public async Task TickAsync(CancellationToken ct)
    {
        if (!gate.IsOpen)
        {
            return;
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IWorkerTickMetrics? metrics = FindTickMetrics(scope.ServiceProvider);
        string outcome = "failed";
        try
        {
            await RunOnceAsync(scope.ServiceProvider, ct);
            outcome = "ok";
        }
        finally
        {
            metrics?.RecordWorkerTick(Name, outcome);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.LogInformation("{Worker} is starting.", Name);
        using var timer = new PeriodicTimer(Interval, timeProvider ?? TimeProvider.System);
        try
        {
            do
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    Logger.LogError(ex, "{Worker} tick failed.", Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        Logger.LogInformation("{Worker} is stopping.", Name);
    }
}

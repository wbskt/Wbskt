using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wbskt.Infrastructure.Telemetry;

namespace Wbskt.Infrastructure.Hosting;

/// <summary>
/// A job a host runs on a fixed interval: once at start (unless <see cref="RunOnStart"/> says
/// otherwise), then every <see cref="Interval"/>. The loop, the gate check, the per-tick scope, the
/// fault guard and the tick metric live here; a worker supplies only what one tick does
/// (<see cref="RunOnceAsync"/>).
/// </summary>
/// <param name="gate">Asked before every tick; a closed gate skips the tick (the engine's standby).</param>
/// <param name="timeProvider">The clock the interval is measured on; the system clock when omitted.</param>
public abstract class PeriodicWorker(IWorkGate gate, IServiceScopeFactory scopeFactory, ILogger logger, TimeProvider? timeProvider = null) : BackgroundService
{
    protected ILogger Logger { get; } = logger;

    protected IServiceScopeFactory ScopeFactory { get; } = scopeFactory;

    /// <summary>The worker's name in logs and the <c>worker</c> tag of the tick metric.</summary>
    protected abstract string Name { get; }

    protected abstract TimeSpan Interval { get; }

    /// <summary>
    /// Whether the first tick runs as the host starts. False for a job whose input is not there yet at
    /// start (the presence sweep, which must let the socket hosts' heartbeats land first).
    /// </summary>
    protected virtual bool RunOnStart => true;

    /// <summary>One tick's work, given the services of a scope opened for it.</summary>
    protected abstract Task RunOnceAsync(IServiceProvider services, CancellationToken ct);

    /// <summary>
    /// Runs one tick: nothing while the gate is closed, otherwise <see cref="RunOnceAsync"/> in a fresh scope.
    /// A failure propagates, so a test sees it; the loop logs it and carries on. The tick is counted on
    /// the host's <see cref="IWorkerTickMetrics"/>, when it registers one; a tick cut short by shutdown
    /// is not counted as failed.
    /// </summary>
    public async Task TickAsync(CancellationToken ct)
    {
        if (!gate.IsOpen)
        {
            return;
        }

        await using AsyncServiceScope scope = ScopeFactory.CreateAsyncScope();
        IWorkerTickMetrics? metrics = scope.ServiceProvider.GetService<IWorkerTickMetrics>();
        string? outcome = "failed";
        try
        {
            await RunOnceAsync(scope.ServiceProvider, ct);
            outcome = "ok";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            outcome = null;
            throw;
        }
        finally
        {
            if (outcome is not null)
            {
                metrics?.RecordWorkerTick(Name, outcome);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (Interval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{Name} has an interval of {Interval}; it must be positive.");
        }

        Logger.LogInformation("{Worker} is starting.", Name);
        using var timer = new PeriodicTimer(Interval, timeProvider ?? TimeProvider.System);
        try
        {
            if (RunOnStart || await timer.WaitForNextTickAsync(stoppingToken))
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
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        Logger.LogInformation("{Worker} is stopping.", Name);
    }
}

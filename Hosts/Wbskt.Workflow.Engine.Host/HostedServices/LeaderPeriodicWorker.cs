using System.Diagnostics;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Telemetry;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

/// <summary>
/// A job the active engine runs on a fixed interval: once at start, then every <see cref="Interval"/>.
/// The loop, the leadership check, the per-tick scope, the fault guard and the tick metric live here;
/// a worker supplies only what one tick does (<see cref="RunOnceAsync"/>).
/// </summary>
public abstract class LeaderPeriodicWorker(ILeadershipGate leadership, IServiceScopeFactory scopeFactory, ILogger logger) : BackgroundService
{
    protected ILogger Logger { get; } = logger;

    /// <summary>The worker's name in logs and the <c>worker</c> tag of the tick metric.</summary>
    protected abstract string Name { get; }

    protected abstract TimeSpan Interval { get; }

    /// <summary>One tick's work, given the services of a scope opened for it.</summary>
    protected abstract Task RunOnceAsync(IServiceProvider services, CancellationToken ct);

    /// <summary>
    /// Runs one tick: nothing on a standby, otherwise <see cref="RunOnceAsync"/> in a fresh scope.
    /// A failure propagates, so a test sees it; the loop logs it and carries on.
    /// </summary>
    public async Task TickAsync(CancellationToken ct)
    {
        if (!leadership.IsActive)
        {
            return;
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        WorkflowMetrics? metrics = scope.ServiceProvider.GetService<WorkflowMetrics>();
        string outcome = "failed";
        try
        {
            await RunOnceAsync(scope.ServiceProvider, ct);
            outcome = "ok";
        }
        finally
        {
            metrics?.WorkerTicks.Add(1, new TagList { { "worker", Name }, { "outcome", outcome } });
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.LogInformation("{Worker} is starting.", Name);
        using var timer = new PeriodicTimer(Interval);
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

namespace Wbskt.Infrastructure.Telemetry;

/// <summary>
/// The tick counter of a host's <see cref="Hosting.PeriodicWorker"/>s, kept on the host's own meter.
/// Tagged <c>worker</c> (the worker's name) and <c>outcome</c> (ok or failed).
/// </summary>
public interface IWorkerTickMetrics
{
    /// <summary>Adds <paramref name="count"/> ticks; zero creates the series without counting a tick.</summary>
    void RecordWorkerTick(string worker, string outcome, long count);
}

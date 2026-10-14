namespace Wbskt.Infrastructure.Telemetry;

/// <summary>
/// The tick counter of a host's <see cref="Hosting.PeriodicWorker"/>s, kept on the host's own meter.
/// Tagged <c>worker</c> (the worker's name) and <c>outcome</c> (ok or failed).
/// </summary>
public interface IWorkerTickMetrics
{
    void RecordWorkerTick(string worker, string outcome);
}

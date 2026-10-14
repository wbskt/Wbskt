using System.Diagnostics;
using System.Diagnostics.Metrics;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Telemetry;

namespace Wbskt.Management.Host.Telemetry;

/// <summary>
/// The management host's own counters: device registrations, device logins and console commands, each
/// by outcome, and the ticks of its periodic workers. The outcome is <c>success</c> or the error code, a
/// fixed set, so it is safe as a label; nothing caller-supplied (a PIN, a client ref) ever is.
/// </summary>
public sealed class ManagementMetrics : IWorkerTickMetrics, IDisposable
{
    public const string MeterName = "Wbskt.Management";
    private readonly Meter _meter;

    public ManagementMetrics()
    {
        _meter = new Meter(MeterName);

        Registrations = _meter.CreateCounter<long>("wbskt_management_device_registrations_total");
        DeviceLogins = _meter.CreateCounter<long>("wbskt_management_device_logins_total");
        Commands = _meter.CreateCounter<long>("wbskt_management_commands_total");
        WorkerTicks = _meter.CreateCounter<long>("wbskt_management_worker_ticks_total");
    }

    public Counter<long> Registrations { get; }
    public Counter<long> DeviceLogins { get; }
    public Counter<long> Commands { get; }

    /// <summary>Ticks of the periodic background workers, tagged <c>worker</c> and <c>outcome</c> (ok or failed).</summary>
    public Counter<long> WorkerTicks { get; }

    public void RecordRegistration(Result result) => Registrations.Add(1, Outcome(result));

    public void RecordDeviceLogin(Result result) => DeviceLogins.Add(1, Outcome(result));

    public void RecordCommand(Result result) => Commands.Add(1, Outcome(result));

    private static KeyValuePair<string, object?> Outcome(Result result) =>
        new("outcome", result.IsSuccess ? "success" : result.Error.Code);

    public void RecordWorkerTick(string worker, string outcome) =>
        WorkerTicks.Add(1, new TagList { { "worker", worker }, { "outcome", outcome } });

    public void Dispose() => _meter.Dispose();
}

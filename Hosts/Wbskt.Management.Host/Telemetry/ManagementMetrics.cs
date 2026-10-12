using System.Diagnostics.Metrics;
using Wbskt.Infrastructure;

namespace Wbskt.Management.Host.Telemetry;

/// <summary>
/// The management host's own counters: device registrations, device logins and console commands, each
/// by outcome. The outcome is <c>success</c> or the error code, a fixed set, so it is safe as a label;
/// nothing caller-supplied (a PIN, a client ref) ever is.
/// </summary>
public sealed class ManagementMetrics : IDisposable
{
    public const string MeterName = "Wbskt.Management";
    private readonly Meter _meter;

    public ManagementMetrics()
    {
        _meter = new Meter(MeterName);

        Registrations = _meter.CreateCounter<long>("wbskt_management_device_registrations_total");
        DeviceLogins = _meter.CreateCounter<long>("wbskt_management_device_logins_total");
        Commands = _meter.CreateCounter<long>("wbskt_management_commands_total");
    }

    public Counter<long> Registrations { get; }
    public Counter<long> DeviceLogins { get; }
    public Counter<long> Commands { get; }

    public void RecordRegistration(Result result) => Registrations.Add(1, Outcome(result));

    public void RecordDeviceLogin(Result result) => DeviceLogins.Add(1, Outcome(result));

    public void RecordCommand(Result result) => Commands.Add(1, Outcome(result));

    private static KeyValuePair<string, object?> Outcome(Result result) =>
        new("outcome", result.IsSuccess ? "success" : result.Error.Code);

    public void Dispose() => _meter.Dispose();
}

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Wbskt.Infrastructure;
using Wbskt.Infrastructure.Telemetry;

namespace Wbskt.Management.Host.Telemetry;

/// <summary>
/// The management host's own counters: client registrations, client logins and console commands, each
/// by outcome, the ticks of its periodic workers, and the deleted workspaces its sweep had to retire. The outcome is <c>success</c> or the error code, a
/// fixed set, so it is safe as a label; nothing caller-supplied (a PIN, a client ref) ever is.
/// </summary>
public sealed class ManagementMetrics : IWorkerTickMetrics, IDisposable
{
    public const string MeterName = "Wbskt.Management";
    private readonly Meter _meter;

    public ManagementMetrics()
    {
        _meter = new Meter(MeterName);

        Registrations = _meter.CreateCounter<long>("wbskt_management_client_registrations_total");
        ClientLogins = _meter.CreateCounter<long>("wbskt_management_client_logins_total");
        Commands = _meter.CreateCounter<long>("wbskt_management_commands_total");
        WorkerTicks = _meter.CreateCounter<long>("wbskt_management_worker_ticks_total");
        WorkspacesRetiredBySweep = _meter.CreateCounter<long>("wbskt_management_workspaces_retired_by_sweep_total");
        CreditGrantsRepaired = _meter.CreateCounter<long>("wbskt_management_credit_grants_repaired_total");
    }

    public Counter<long> Registrations { get; }
    public Counter<long> ClientLogins { get; }
    public Counter<long> Commands { get; }

    /// <summary>Ticks of the periodic background workers, tagged <c>worker</c> and <c>outcome</c> (ok or failed).</summary>
    public Counter<long> WorkerTicks { get; }

    /// <summary>
    /// Deleted workspaces the sweep found still live and retired: each one a <c>WorkspaceDeletedEvent</c>
    /// that was lost or whose handling gave up. Zero is the healthy value.
    /// </summary>
    public Counter<long> WorkspacesRetiredBySweep { get; }

    /// <summary>
    /// Credit grant changes the hourly reconcile had to make, tagged <c>change</c> (granted, revoked, held
    /// or released): each a grant event that was lost or whose handling gave up. Zero is the healthy value.
    /// </summary>
    public Counter<long> CreditGrantsRepaired { get; }

    public void RecordRegistration(Result result) => Registrations.Add(1, Outcome(result));

    public void RecordClientLogin(Result result) => ClientLogins.Add(1, Outcome(result));

    public void RecordCommand(Result result) => Commands.Add(1, Outcome(result));

    private static KeyValuePair<string, object?> Outcome(Result result) =>
        new("outcome", result.IsSuccess ? "success" : result.Error.Code);

    public void RecordWorkerTick(string worker, string outcome, long count) =>
        WorkerTicks.Add(count, new TagList { { "worker", worker }, { "outcome", outcome } });

    public void RecordWorkspaceRetiredBySweep() => WorkspacesRetiredBySweep.Add(1);

    public void RecordCreditGrantRepaired(string change) => CreditGrantsRepaired.Add(1, new KeyValuePair<string, object?>("change", change));

    public void Dispose() => _meter.Dispose();
}

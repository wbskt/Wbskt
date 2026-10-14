using Microsoft.Extensions.Options;
using Wbskt.Infrastructure.Hosting;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Telemetry;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// The backstop behind <c>WorkspaceDeletedEvent</c>. Daily, asks the auth host which workspaces were
/// deleted in the last <see cref="WorkspaceReconciliationOptions.LookbackDays"/> days and retires, with
/// the same <see cref="WorkspaceRetirer"/> the event's handler uses, any that still have live
/// policies, clients, workflows or runs here. Each one it retires is counted
/// (<c>wbskt_management_workspaces_retired_by_sweep_total</c>): an event that was lost, before the
/// outbox existed, or whose handler ran out of retries.
/// </summary>
/// <remarks>
/// <para>
/// No watermark: every sweep asks for the whole window, and a workspace already retired is passed
/// over because <c>dbo.Workspace_HasLiveContent</c> answers no for it. So only real repairs are
/// counted, and a sweep that fails is made up by the next. A delete younger than
/// <see cref="WorkspaceReconciliationOptions.GracePeriod"/> is left to its event.
/// </para>
/// <para>
/// Each management replica runs it. Retiring is safe to repeat, so two replicas sweeping at once
/// cost a duplicate cancel at worst, and may both count the same repair.
/// </para>
/// </remarks>
public sealed class DeletedWorkspaceSweeper : PeriodicWorker
{
    private readonly TimeProvider _timeProvider;
    private readonly ManagementMetrics _metrics;
    private readonly TimeSpan _lookback;
    private readonly TimeSpan _grace;

    public DeletedWorkspaceSweeper(
        IServiceScopeFactory scopeFactory,
        IOptions<WorkspaceReconciliationOptions> options,
        ManagementMetrics metrics,
        ILogger<DeletedWorkspaceSweeper> logger)
        : this(scopeFactory, TimeProvider.System, options, metrics, logger)
    {
    }

    internal DeletedWorkspaceSweeper(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<WorkspaceReconciliationOptions> options,
        ManagementMetrics metrics,
        ILogger<DeletedWorkspaceSweeper> logger)
        : base(AlwaysOpenWorkGate.Instance, scopeFactory, logger, timeProvider)
    {
        _timeProvider = timeProvider;
        _metrics = metrics;
        Interval = options.Value.SweepInterval;
        _lookback = TimeSpan.FromDays(Math.Max(1, options.Value.LookbackDays));
        _grace = options.Value.GracePeriod < TimeSpan.Zero ? TimeSpan.Zero : options.Value.GracePeriod;
    }

    protected override string Name => "Deleted workspace sweep";

    protected override TimeSpan Interval { get; }

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        await SweepAsync(services, ct);
    }

    /// <summary>One sweep in a scope of its own; see <see cref="SweepAsync(IServiceProvider, CancellationToken)"/>.</summary>
    internal async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        return await SweepAsync(scope.ServiceProvider, cancellationToken);
    }

    /// <summary>
    /// One sweep. Returns how many workspaces it retired. Throws when the auth host cannot be asked,
    /// or when any workspace could not be retired (after trying the rest), so the tick is counted as
    /// failed; the next sweep asks for the same window again.
    /// </summary>
    private async Task<int> SweepAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var auth = services.GetRequiredService<IDeletedWorkspacesClient>();
        var provider = services.GetRequiredService<IWorkspaceRetirementProvider>();
        var retirer = services.GetRequiredService<WorkspaceRetirer>();

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var deleted = await auth.GetDeletedBetweenAsync(nowUtc - _lookback, nowUtc - _grace, cancellationToken);

        int retired = 0;
        var failed = new List<int>();
        foreach (int workspaceId in deleted)
        {
            try
            {
                if (!await provider.HasLiveContentAsync(workspaceId, cancellationToken))
                {
                    continue;
                }

                Logger.LogWarning(
                    "Workspace {WorkspaceId} was deleted but still had live content here; its WorkspaceDeletedEvent was lost or not handled. Retiring it now.",
                    workspaceId);
                await retirer.RetireAsync(workspaceId, cancellationToken);
                _metrics.RecordWorkspaceRetiredBySweep();
                retired++;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                // One workspace that cannot be retired (the broker refusing a cancel) must not keep the
                // others live for another day.
                Logger.LogError(ex, "The deleted workspace sweep could not retire workspace {WorkspaceId}.", workspaceId);
                failed.Add(workspaceId);
            }
        }

        if (deleted.Count > 0)
        {
            Logger.LogInformation(
                "Deleted workspace sweep: {DeletedCount} workspace(s) deleted in the window, {RetiredCount} still live and retired.",
                deleted.Count, retired);
        }

        if (failed.Count > 0)
        {
            throw new InvalidOperationException(
                $"The deleted workspace sweep could not retire {failed.Count} workspace(s): {string.Join(", ", failed)}. The next sweep tries again.");
        }

        return retired;
    }
}

using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Runtime;
using Wbskt.Workflow.Telemetry;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

// Leader-only: the standby polled the database too and, holding no work, reported a dispatcher
// depth of zero under the same metric names as the leader's real one.
internal sealed class MetricsExporter(
    TimeProvider clock,
    ILeadershipGate leadership,
    IServiceScopeFactory scopeFactory,
    WorkflowMetrics workflowMetrics,
    ChannelRunDispatcher dispatcher,
    ILogger<MetricsExporter> logger,
    IOptions<WorkflowEngineOptions> options) : LeaderPeriodicWorker(leadership, scopeFactory, logger)
{
    protected override string Name => "Metrics exporter";

    protected override TimeSpan Interval { get; } = options.Value.MetricsExportInterval;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var runCountersProvider = services.GetRequiredService<IRunCountersProvider>();
        var bookmarkProvider = services.GetRequiredService<IBookmarkProvider>();
        var pendingTriggerEventProvider = services.GetRequiredService<IPendingTriggerEventProvider>();

        long activeBranches = await runCountersProvider.SumActiveBranchesAsync(ct);
        long pendingTriggerDepth = await pendingTriggerEventProvider.CountAllAsync(ct);
        long queueDepth = dispatcher.Count;
        IReadOnlyDictionary<string, long> bookmarksByWakeKind = await bookmarkProvider.CountGroupedByWakeKindAsync(ct);

        workflowMetrics.UpdateSnapshot(activeBranches, queueDepth, pendingTriggerDepth, bookmarksByWakeKind);

        // A minute of grace: the poller claims on its own interval, so a bookmark a few seconds past
        // due is normal; one a minute past due is not.
        DateTime overdueCutoffUtc = clock.GetUtcNow().UtcDateTime.AddMinutes(-1);
        long overdueBookmarks = await bookmarkProvider.CountOverdueAsync(overdueCutoffUtc, ct);
        workflowMetrics.UpdateOverdueBookmarks(overdueBookmarks);

        Logger.LogInformation(
            "Workflow metrics exported: ActiveBranches={ActiveBranches}, DispatcherQueueDepth={QueueDepth}, PendingTriggerDepth={PendingTriggerDepth}, BookmarksCount={BookmarksCount}, OverdueBookmarks={OverdueBookmarks}",
            activeBranches,
            queueDepth,
            pendingTriggerDepth,
            bookmarksByWakeKind.Values.Sum(),
            overdueBookmarks);
    }
}

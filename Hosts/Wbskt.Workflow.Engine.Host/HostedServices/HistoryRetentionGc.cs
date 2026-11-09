using Microsoft.Extensions.Options;
using Wbskt.Infrastructure.Billing;
using Wbskt.Models.Billing;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class HistoryRetentionGc(
    TimeProvider clock,
    ILeadershipGate leadership,
    IServiceScopeFactory scopeFactory,
    ILogger<HistoryRetentionGc> logger,
    IOptions<WorkflowEngineOptions> options,
    PlanCatalog catalog) : LeaderPeriodicWorker(leadership, scopeFactory, logger)
{
    private const int BatchSize = 5000;
    // Each run takes its branches and history with it, so a run batch is much heavier than a row batch.
    internal const int RunBatchSize = 100;
    private readonly TimeSpan _retentionWindow = options.Value.HistoryRetentionWindow;
    private readonly TimeSpan _elevatedRetentionWindow = options.Value.HistoryRetentionWindowElevated;

    protected override string Name => "History retention GC";

    protected override TimeSpan Interval { get; } = options.Value.HistoryRetentionInterval;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var historyEventProvider = services.GetRequiredService<IHistoryEventProvider>();
        DateTime nowUtc = clock.GetUtcNow().UtcDateTime;
        DateTime cutoffUtc = nowUtc - _retentionWindow;
        DateTime elevatedCutoffUtc = nowUtc - _elevatedRetentionWindow;
        while (true)
        {
            int deleted = await historyEventProvider.DeleteForRetiredRunsAsync(cutoffUtc, BatchSize, elevatedCutoffUtc, ct);
            if (deleted < BatchSize)
            {
                break;
            }
        }

        // A run goes, with all its history, once its workspace's plan stops keeping it, and in any case
        // once even its Warn/Error history is past the elevated window.
        IReadOnlyCollection<RunHistoryRetention> retention = Enum.GetValues<PlanCode>()
            .Select(plan => new RunHistoryRetention(plan, catalog.LimitsFor(plan).RunHistoryDays))
            .ToList();
        var runRetentionProvider = services.GetService<IRunRetentionProvider>();
        if (runRetentionProvider is null)
        {
            return;
        }

        while (true)
        {
            int deleted = await runRetentionProvider.DeleteRetiredRunsAsync(nowUtc, retention, elevatedCutoffUtc, RunBatchSize, ct);
            if (deleted < RunBatchSize)
            {
                return;
            }
        }
    }
}

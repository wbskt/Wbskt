using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class HistoryRetentionGc(
    IClock clock,
    ILeadershipGate leadership,
    IServiceScopeFactory scopeFactory,
    ILogger<HistoryRetentionGc> logger,
    IOptions<WorkflowEngineOptions> options) : LeaderPeriodicWorker(leadership, scopeFactory, logger)
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
        DateTime cutoffUtc = clock.UtcNow - _retentionWindow;
        DateTime elevatedCutoffUtc = clock.UtcNow - _elevatedRetentionWindow;
        while (true)
        {
            int deleted = await historyEventProvider.DeleteForRetiredRunsAsync(cutoffUtc, BatchSize, elevatedCutoffUtc, ct);
            if (deleted < BatchSize)
            {
                break;
            }
        }

        // Once even a run's Warn/Error history is past retention, the run row and its branches are
        // all that is left; without this they were kept forever, full branch state included.
        var runRetentionProvider = services.GetService<IRunRetentionProvider>();
        if (runRetentionProvider is null)
        {
            return;
        }

        while (true)
        {
            int deleted = await runRetentionProvider.DeleteRetiredRunsAsync(elevatedCutoffUtc, RunBatchSize, ct);
            if (deleted < RunBatchSize)
            {
                return;
            }
        }
    }
}

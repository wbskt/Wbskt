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
    // Each run takes its branches and history with it, so a batch is a hundred runs.
    internal const int RunBatchSize = 100;
    private readonly TimeSpan _elevatedRetentionWindow = options.Value.HistoryRetentionWindowElevated;

    protected override string Name => "History retention GC";

    protected override TimeSpan Interval { get; } = options.Value.HistoryRetentionInterval;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        DateTime nowUtc = clock.GetUtcNow().UtcDateTime;
        DateTime elevatedCutoffUtc = nowUtc - _elevatedRetentionWindow;

        // A run goes, with all its history, once its workspace's plan stops keeping it, and in any case
        // past the elevated window. History is never pruned apart from its run, so a run the plan still
        // keeps is kept whole.
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

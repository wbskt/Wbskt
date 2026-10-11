using System.Text.Json;
using Cronos;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;
using Wbskt.Workflow.Channels;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class ScheduledFireTicker(
    IClock clock,
    ILeadershipGate leadership,
    IServiceScopeFactory scopeFactory,
    ILogger<ScheduledFireTicker> logger,
    IOptions<WorkflowEngineOptions> options) : LeaderPeriodicWorker(leadership, scopeFactory, logger)
{
    private readonly int _leaseSeconds = options.Value.LeaseDurationSeconds;
    private readonly int _batchSize = options.Value.ScheduledFireLeaseBatchSize;

    protected override string Name => "Scheduled fire ticker";

    protected override TimeSpan Interval { get; } = options.Value.ScheduleTickInterval;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var scheduledFireProvider = services.GetRequiredService<IScheduledFireProvider>();
        var inboundHub = services.GetRequiredService<IInboundHub>();
        IReadOnlyCollection<ScheduledFireRow> fires = await scheduledFireProvider.LeaseDueAsync(_leaseSeconds, _batchSize, ct);
        if (fires.Count > 0)
        {
            Logger.LogInformation("Leased {Count} scheduled fires for dispatch", fires.Count);
        }
        DateTime now = clock.UtcNow;

        foreach (ScheduledFireRow fire in fires)
        {
            try
            {
                InboundEvent inboundEvent = ScheduleChannel.Event(fire, now);

                Logger.LogInformation("Dispatching scheduled fire {FireId} for workflow {WorkflowRefId}", fire.Id, fire.WorkflowRefId);
                await inboundHub.HandleAsync(inboundEvent, ct);

                (bool parseFailed, DateTime? nextOccurrence) = ComputeNextOccurrence(fire);
                if (parseFailed)
                {
                    Logger.LogError("Scheduled fire {FireId} has an unparseable cron expression '{Cron}'; leaving it in place for investigation.", fire.Id, fire.CronOrInterval);
                    continue;
                }

                if (nextOccurrence.HasValue)
                {
                    Logger.LogDebug("Advancing scheduled fire {FireId} to {NextOccurrence}", fire.Id, nextOccurrence.Value);
                    await scheduledFireProvider.AdvanceNextAsync(fire.Id, nextOccurrence.Value, ct);
                    continue;
                }

                Logger.LogDebug("Deleting one-time scheduled fire {FireId}", fire.Id);
                await scheduledFireProvider.DeleteByIdAsync(fire.Id, ct);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to process scheduled fire {FireId}", fire.Id);
            }
        }
    }

    /// <summary>
    /// Returns (ParseFailed, Next). ParseFailed means the cron expression itself is unparseable
    /// (a bug worth surfacing, not a reason to delete the schedule). A null Next with ParseFailed
    /// false means the expression parsed but legitimately has no further occurrences (or is blank) —
    /// a one-time schedule that should be deleted.
    /// </summary>
    private static (bool ParseFailed, DateTime? Next) ComputeNextOccurrence(ScheduledFireRow fire)
    {
        if (string.IsNullOrWhiteSpace(fire.CronOrInterval))
        {
            return (false, null);
        }

        if (!CronParser.TryParse(fire.CronOrInterval, out CronExpression? cron))
        {
            return (true, null);
        }

        // Cronos insists on a Utc-kind DateTime; a value read back from SQL is Unspecified.
        return (false, cron!.GetNextOccurrence(DateTime.SpecifyKind(fire.NextFireAt, DateTimeKind.Utc), TimeZoneInfo.Utc));
    }
}

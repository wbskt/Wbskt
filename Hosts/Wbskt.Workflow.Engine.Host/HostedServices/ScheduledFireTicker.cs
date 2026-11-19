using Wbskt.Models;
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
    TimeProvider clock,
    ILeadershipGate leadership,
    IServiceScopeFactory scopeFactory,
    ILogger<ScheduledFireTicker> logger,
    IOptions<WorkflowEngineOptions> options) : LeaderPeriodicWorker(leadership, scopeFactory, logger, clock)
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
        DateTime now = clock.GetUtcNow().UtcDateTime;

        foreach (ScheduledFireRow fire in fires)
        {
            try
            {
                ScheduledFireRow occurrence = LatestMissed(fire, now);
                if (occurrence.NextFireAt != fire.NextFireAt)
                {
                    Logger.LogInformation(
                        "Scheduled fire {FireId} missed the occurrences from {Missed} to before {Latest} while no leader ran; firing {Latest} once instead.",
                        fire.Id,
                        fire.NextFireAt,
                        occurrence.NextFireAt);
                }

                InboundEvent inboundEvent = ScheduleChannel.Event(occurrence, now);

                Logger.LogInformation("Dispatching scheduled fire {FireId} for workflow {WorkflowRefId}", fire.Id, fire.WorkflowRefId);
                await inboundHub.HandleAsync(inboundEvent, ct);

                (bool parseFailed, DateTime? nextOccurrence) = ComputeNextOccurrence(occurrence);
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
    /// The occurrence to fire for a due row. When the engine was down for more than one interval the row
    /// is behind by several occurrences; firing each one would start a backlog of stale runs (an
    /// every-minute schedule down for two hours: 120 runs, each charged), so only the latest one that has
    /// come due is fired, and the schedule then goes on from after it. A row due for one occurrence is
    /// returned as it is.
    /// </summary>
    private static ScheduledFireRow LatestMissed(ScheduledFireRow fire, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(fire.CronOrInterval) || !CronParser.TryParse(fire.CronOrInterval, out CronExpression? cron))
        {
            return fire;
        }

        DateTime from = fire.NextFireAt.AsUtc();
        DateTime to = now.AsUtc();
        if (to <= from)
        {
            return fire;
        }

        DateTime? latest = null;
        foreach (DateTime missed in cron!.GetOccurrences(from, to, TimeZoneInfo.Utc, fromInclusive: false, toInclusive: true))
        {
            latest = missed;
        }

        return latest is { } occurrence ? fire with { NextFireAt = occurrence } : fire;
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
        return (false, cron!.GetNextOccurrence(fire.NextFireAt.AsUtc(), TimeZoneInfo.Utc));
    }
}

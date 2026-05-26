using System.Text.Json;
using Cronos;
using Microsoft.Extensions.Hosting;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class ScheduledFireTicker : BackgroundService
{
    private const string LeaseName = "schedule-tick";
    private const int LeaseSeconds = 120;
    private const int BatchSize = 64;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private readonly IClock _clock;
    private readonly ILeaseHolder _leaseHolder;
    private readonly IScheduledFireProvider _scheduledFireProvider;
    private readonly IInboundHub _inboundHub;
    private readonly ILogger<ScheduledFireTicker> _logger;

    public ScheduledFireTicker(
        IClock clock,
        ILeaseHolder leaseHolder,
        IScheduledFireProvider scheduledFireProvider,
        IInboundHub inboundHub,
        ILogger<ScheduledFireTicker> logger)
    {
        _clock = clock;
        _leaseHolder = leaseHolder;
        _scheduledFireProvider = scheduledFireProvider;
        _inboundHub = inboundHub;
        _logger = logger;
    }

    public async Task ProcessScheduledFiresAsync(CancellationToken ct)
    {
        if (!await _leaseHolder.IsHeldAsync(LeaseName, ct))
        {
            return;
        }

        IReadOnlyCollection<ScheduledFireRow> fires = await _scheduledFireProvider.LeaseDueAsync(LeaseSeconds, BatchSize, ct);
        DateTime now = _clock.UtcNow;

        foreach (ScheduledFireRow fire in fires)
        {
            var payload = new Dictionary<string, JsonElement>
            {
                ["scheduledFireId"] = JsonSerializer.SerializeToElement(fire.Id),
                ["definitionRefId"] = JsonSerializer.SerializeToElement(fire.WorkflowRefId),
                ["fireAt"] = JsonSerializer.SerializeToElement(fire.NextFireAt)
            };
            var inboundEvent = new InboundEvent(
                "schedule",
                $"schedule:{fire.Id}",
                Guid.NewGuid().ToString(),
                payload,
                now);

            await _inboundHub.HandleAsync(inboundEvent, ct);

            DateTime? nextOccurrence = ComputeNextOccurrence(fire);
            if (nextOccurrence.HasValue)
            {
                await _scheduledFireProvider.AdvanceNextAsync(fire.Id, nextOccurrence.Value, ct);
                continue;
            }

            await _scheduledFireProvider.DeleteByIdAsync(fire.Id, ct);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ExecuteGuardedAsync(stoppingToken);

        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ExecuteGuardedAsync(stoppingToken);
        }
    }

    private async Task ExecuteGuardedAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ProcessScheduledFiresAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled fire ticker tick failed.");
        }
    }

    private static DateTime? ComputeNextOccurrence(ScheduledFireRow fire)
    {
        if (string.IsNullOrWhiteSpace(fire.CronOrInterval))
        {
            return null;
        }

        try
        {
            CronExpression cron = CronExpression.Parse(fire.CronOrInterval, CronFormat.IncludeSeconds);
            return cron.GetNextOccurrence(fire.NextFireAt, TimeZoneInfo.Utc);
        }
        catch (CronFormatException)
        {
            return null;
        }
    }
}

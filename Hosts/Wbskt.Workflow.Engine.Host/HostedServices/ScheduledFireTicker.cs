using System.Text.Json;
using Cronos;
using Microsoft.Extensions.Options;
using Wbskt.Workflow.Abstraction.Configuration;
using Wbskt.Workflow.Abstraction.Entities;
using Wbskt.Workflow.Abstraction.Engine;
using Wbskt.Workflow.Abstraction.Providers;
using Wbskt.Workflow.Abstraction.Runtime;

namespace Wbskt.Workflow.Engine.Host.HostedServices;

public sealed class ScheduledFireTicker : BackgroundService
{
    private const string LeaseName = "schedule-tick";
    private readonly IClock _clock;
    private readonly ILeaseHolder _leaseHolder;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledFireTicker> _logger;
    private readonly int _leaseSeconds;
    private readonly int _batchSize;
    private readonly TimeSpan _pollInterval;

    [ActivatorUtilitiesConstructor]
    public ScheduledFireTicker(
        IClock clock,
        ILeaseHolder leaseHolder,
        IServiceScopeFactory scopeFactory,
        ILogger<ScheduledFireTicker> logger,
        IOptions<WorkflowEngineOptions> options)
        : this(
            clock,
            leaseHolder,
            scopeFactory,
            logger,
            options.Value.LeaseDurationSeconds,
            options.Value.ScheduledFireLeaseBatchSize,
            options.Value.ScheduleTickInterval)
    {
    }

    internal ScheduledFireTicker(
        IClock clock,
        ILeaseHolder leaseHolder,
        IScheduledFireProvider scheduledFireProvider,
        IInboundHub inboundHub,
        ILogger<ScheduledFireTicker> logger,
        int leaseSeconds = 120,
        int batchSize = 64,
        TimeSpan? pollInterval = null)
        : this(
            clock,
            leaseHolder,
            new StaticScopeFactory(scheduledFireProvider, inboundHub),
            logger,
            leaseSeconds,
            batchSize,
            pollInterval)
    {
    }

    private ScheduledFireTicker(
        IClock clock,
        ILeaseHolder leaseHolder,
        IServiceScopeFactory scopeFactory,
        ILogger<ScheduledFireTicker> logger,
        int leaseSeconds,
        int batchSize,
        TimeSpan? pollInterval)
    {
        _clock = clock;
        _leaseHolder = leaseHolder;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _leaseSeconds = leaseSeconds;
        _batchSize = batchSize;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(1);
    }

    public async Task ProcessScheduledFiresAsync(CancellationToken ct)
    {
        if (!await _leaseHolder.IsHeldAsync(LeaseName, ct))
        {
            return;
        }

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        var scheduledFireProvider = scope.ServiceProvider.GetRequiredService<IScheduledFireProvider>();
        var inboundHub = scope.ServiceProvider.GetRequiredService<IInboundHub>();
        IReadOnlyCollection<ScheduledFireRow> fires = await scheduledFireProvider.LeaseDueAsync(_leaseSeconds, _batchSize, ct);
        if (fires.Count > 0)
        {
            _logger.LogInformation("Leased {Count} scheduled fires for dispatch", fires.Count);
        }
        DateTime now = _clock.UtcNow;

        foreach (ScheduledFireRow fire in fires)
        {
            try
            {
                var payload = new Dictionary<string, JsonElement>
                {
                    ["scheduledFireId"] = JsonSerializer.SerializeToElement(fire.Id),
                    ["definitionRefId"] = JsonSerializer.SerializeToElement(fire.WorkflowRefId),
                    ["fireAt"] = JsonSerializer.SerializeToElement(fire.NextFireAt)
                };
                var inboundEvent = new InboundEvent(
                    "schedule",
                    [$"schedule:{fire.Id}"],
                    // Derived from the occurrence, not random: if the process dies between this dispatch
                    // and AdvanceNextAsync below, the lease expires and the same occurrence is leased
                    // again, and only a stable id lets the dispatcher's idempotency claim drop it.
                    OccurrenceEventId(fire),
                    payload,
                    now);

                _logger.LogInformation("Dispatching scheduled fire {FireId} for workflow {WorkflowRefId}", fire.Id, fire.WorkflowRefId);
                await inboundHub.HandleAsync(inboundEvent, ct);

                (bool parseFailed, DateTime? nextOccurrence) = ComputeNextOccurrence(fire);
                if (parseFailed)
                {
                    _logger.LogError("Scheduled fire {FireId} has an unparseable cron expression '{Cron}'; leaving it in place for investigation.", fire.Id, fire.CronOrInterval);
                    continue;
                }

                if (nextOccurrence.HasValue)
                {
                    _logger.LogDebug("Advancing scheduled fire {FireId} to {NextOccurrence}", fire.Id, nextOccurrence.Value);
                    await scheduledFireProvider.AdvanceNextAsync(fire.Id, nextOccurrence.Value, ct);
                    continue;
                }

                _logger.LogDebug("Deleting one-time scheduled fire {FireId}", fire.Id);
                await scheduledFireProvider.DeleteByIdAsync(fire.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process scheduled fire {FireId}", fire.Id);
            }
        }
    }

    internal static string OccurrenceEventId(ScheduledFireRow fire) =>
        $"schedule:{fire.Id}:{fire.NextFireAt:yyyyMMddTHHmmssfff}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduled fire ticker is starting.");
        await ExecuteGuardedAsync(stoppingToken);

        using var timer = new PeriodicTimer(_pollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ExecuteGuardedAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Scheduled fire ticker is stopping.");
            throw;
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

        return (false, cron!.GetNextOccurrence(fire.NextFireAt, TimeZoneInfo.Utc));
    }

    private sealed class StaticScopeFactory(IScheduledFireProvider scheduledFireProvider, IInboundHub inboundHub) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            return new StaticServiceScope(scheduledFireProvider, inboundHub);
        }
    }

    private sealed class StaticServiceScope(IScheduledFireProvider scheduledFireProvider, IInboundHub inboundHub) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider { get; } = new StaticServiceProvider(scheduledFireProvider, inboundHub);

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StaticServiceProvider(IScheduledFireProvider scheduledFireProvider, IInboundHub inboundHub) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IScheduledFireProvider))
            {
                return scheduledFireProvider;
            }

            if (serviceType == typeof(IInboundHub))
            {
                return inboundHub;
            }

            return null;
        }
    }
}

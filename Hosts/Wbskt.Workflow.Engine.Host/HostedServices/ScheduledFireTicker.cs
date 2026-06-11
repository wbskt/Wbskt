using System.Text.Json;
using Cronos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
                [$"schedule:{fire.Id}"],
                Guid.NewGuid().ToString(),
                payload,
                now);

            await inboundHub.HandleAsync(inboundEvent, ct);

            DateTime? nextOccurrence = ComputeNextOccurrence(fire);
            if (nextOccurrence.HasValue)
            {
                await scheduledFireProvider.AdvanceNextAsync(fire.Id, nextOccurrence.Value, ct);
                continue;
            }

            await scheduledFireProvider.DeleteByIdAsync(fire.Id, ct);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ExecuteGuardedAsync(stoppingToken);

        using var timer = new PeriodicTimer(_pollInterval);
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

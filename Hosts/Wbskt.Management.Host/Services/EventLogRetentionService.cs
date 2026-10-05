using Microsoft.Extensions.Options;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// Deletes event log entries older than <see cref="EventLoggingOptions.RetentionDays"/>, in batches,
/// on <see cref="EventLoggingOptions.RetentionInterval"/>. Raw device traffic (events marked
/// <see cref="DeviceTrafficAttribute"/>) goes sooner, after
/// <see cref="EventLoggingOptions.DeviceTrafficRetentionDays"/>: it is most of the table, and the
/// audit records (sign-ins, policy and workflow changes) are what is worth keeping longer.
/// </summary>
public sealed class EventLogRetentionService : BackgroundService
{
    internal const int BatchSize = 5000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EventLogRetentionService> _logger;
    private readonly TimeSpan _retention;
    private readonly TimeSpan _deviceTrafficRetention;
    private readonly TimeSpan _interval;

    public EventLogRetentionService(
        IServiceScopeFactory scopeFactory,
        IOptions<EventLoggingOptions> options,
        ILogger<EventLogRetentionService> logger)
        : this(scopeFactory, TimeProvider.System, options, logger)
    {
    }

    internal EventLogRetentionService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<EventLoggingOptions> options,
        ILogger<EventLogRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
        _retention = TimeSpan.FromDays(options.Value.RetentionDays);
        _deviceTrafficRetention = TimeSpan.FromDays(Math.Min(options.Value.DeviceTrafficRetentionDays, options.Value.RetentionDays));
        _interval = options.Value.RetentionInterval;
    }

    /// <summary>One sweep: device traffic past its window, then everything past the audit window. Returns the total.</summary>
    internal async Task<long> SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<IEventProvider>();
        var registry = scope.ServiceProvider.GetRequiredService<IEventRegistry>();
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        long total = 0;
        if (_deviceTrafficRetention < _retention)
        {
            // Only the device traffic events this database has seen; one never logged has no id yet.
            int[] deviceTrafficIds = DeviceTrafficAttribute.EventNames
                .Select(registry.GetEventId)
                .Where(id => id > 0)
                .ToArray();
            if (deviceTrafficIds.Length > 0)
            {
                total += await DeleteAsync(provider, nowUtc - _deviceTrafficRetention, deviceTrafficIds, "device traffic", cancellationToken);
            }
        }

        total += await DeleteAsync(provider, nowUtc - _retention, null, "all events", cancellationToken);
        return total;
    }

    /// <summary>Deletes batch after batch until one comes back short.</summary>
    private async Task<long> DeleteAsync(IEventProvider provider, DateTime cutoffUtc, IReadOnlyCollection<int>? eventIds, string scope, CancellationToken cancellationToken)
    {
        long total = 0;
        while (true)
        {
            var deleted = await provider.DeleteBeforeAsync(cutoffUtc, BatchSize, eventIds, cancellationToken);
            total += deleted;
            if (deleted < BatchSize)
            {
                break;
            }
        }

        if (total > 0)
        {
            _logger.LogInformation("Event log retention deleted {Count} entries ({Scope}) older than {CutoffUtc:o}.", total, scope, cutoffUtc);
        }

        return total;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SweepGuardedAsync(stoppingToken);

        using var timer = new PeriodicTimer(_interval, _timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SweepGuardedAsync(stoppingToken);
        }
    }

    private async Task SweepGuardedAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SweepAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The next tick retries; a failed sweep only lets the table grow for an interval longer.
            _logger.LogError(ex, "Event log retention sweep failed.");
        }
    }
}

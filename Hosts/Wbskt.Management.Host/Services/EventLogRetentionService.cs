using Microsoft.Extensions.Options;
using Wbskt.EventBus.Abstractions;
using Wbskt.Events;
using Wbskt.Infrastructure.Hosting;
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
public sealed class EventLogRetentionService : PeriodicWorker
{
    internal const int BatchSize = 5000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EventLogRetentionService> _logger;
    private readonly TimeSpan _retention;
    private readonly TimeSpan _deviceTrafficRetention;

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
        : base(AlwaysOpenWorkGate.Instance, scopeFactory, logger, timeProvider)
    {
        Interval = options.Value.RetentionInterval;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
        _retention = TimeSpan.FromDays(options.Value.RetentionDays);
        _deviceTrafficRetention = TimeSpan.FromDays(Math.Min(options.Value.DeviceTrafficRetentionDays, options.Value.RetentionDays));
    }

    protected override string Name => "Event log retention";

    // A failed sweep is retried on the next tick; it only lets the table grow for an interval longer.
    protected override TimeSpan Interval { get; }

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        await SweepAsync(services, ct);
    }

    /// <summary>One sweep in a scope of its own; see <see cref="SweepAsync(IServiceProvider, CancellationToken)"/>.</summary>
    internal async Task<long> SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        return await SweepAsync(scope.ServiceProvider, cancellationToken);
    }

    /// <summary>One sweep: device traffic past its window, then everything past the audit window. Returns the total.</summary>
    private async Task<long> SweepAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var provider = services.GetRequiredService<IEventProvider>();
        var registry = services.GetRequiredService<IEventRegistry>();
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
}

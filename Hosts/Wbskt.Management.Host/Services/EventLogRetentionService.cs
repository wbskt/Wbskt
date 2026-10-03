using Microsoft.Extensions.Options;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Services;

/// <summary>
/// Deletes event log entries older than <see cref="EventLoggingOptions.RetentionDays"/>, in batches,
/// on <see cref="EventLoggingOptions.RetentionInterval"/>. Without it the table grows with every
/// device message for as long as the deployment lives.
/// </summary>
public sealed class EventLogRetentionService : BackgroundService
{
    internal const int BatchSize = 5000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EventLogRetentionService> _logger;
    private readonly TimeSpan _retention;
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
        _interval = options.Value.RetentionInterval;
    }

    /// <summary>One sweep: deletes batch after batch until one comes back short. Returns the total.</summary>
    internal async Task<long> SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetRequiredService<IEventProvider>();
        var cutoffUtc = _timeProvider.GetUtcNow().UtcDateTime - _retention;

        long total = 0;
        while (true)
        {
            var deleted = await provider.DeleteBeforeAsync(cutoffUtc, BatchSize, cancellationToken);
            total += deleted;
            if (deleted < BatchSize)
            {
                break;
            }
        }

        if (total > 0)
        {
            _logger.LogInformation("Event log retention deleted {Count} entries older than {CutoffUtc:o}.", total, cutoffUtc);
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

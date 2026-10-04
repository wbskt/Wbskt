using Microsoft.Extensions.Options;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Services.Readings;

/// <summary>
/// Deletes readings received more than <see cref="ReadingsOptions.RetentionDays"/> ago, in batches,
/// on <see cref="ReadingsOptions.RetentionInterval"/>. Readings of a deleted client go the same way.
/// </summary>
public sealed class ClientReadingRetentionService : BackgroundService
{
    internal const int BatchSize = 5000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClientReadingRetentionService> _logger;
    private readonly TimeSpan _retention;
    private readonly TimeSpan _interval;

    public ClientReadingRetentionService(
        IServiceScopeFactory scopeFactory,
        IOptions<ReadingsOptions> options,
        ILogger<ClientReadingRetentionService> logger)
        : this(scopeFactory, TimeProvider.System, options, logger)
    {
    }

    internal ClientReadingRetentionService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<ReadingsOptions> options,
        ILogger<ClientReadingRetentionService> logger)
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
        var provider = scope.ServiceProvider.GetRequiredService<IClientReadingProvider>();
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
            _logger.LogInformation("Readings retention deleted {Count} readings received before {CutoffUtc:o}.", total, cutoffUtc);
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
            _logger.LogError(ex, "Readings retention sweep failed.");
        }
    }
}

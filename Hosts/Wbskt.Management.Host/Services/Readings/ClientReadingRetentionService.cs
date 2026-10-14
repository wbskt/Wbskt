using Microsoft.Extensions.Options;
using Wbskt.Infrastructure.Hosting;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;

namespace Wbskt.Management.Host.Services.Readings;

/// <summary>
/// Deletes readings received more than <see cref="ReadingsOptions.RetentionDays"/> ago, in batches,
/// on <see cref="ReadingsOptions.RetentionInterval"/>. Readings of a deleted client go the same way.
/// </summary>
public sealed class ClientReadingRetentionService : PeriodicWorker
{
    internal const int BatchSize = 5000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClientReadingRetentionService> _logger;
    private readonly TimeSpan _retention;

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
        : base(AlwaysOpenWorkGate.Instance, scopeFactory, logger, timeProvider)
    {
        Interval = options.Value.RetentionInterval;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
        _retention = TimeSpan.FromDays(options.Value.RetentionDays);
    }

    protected override string Name => "Readings retention";

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

    /// <summary>One sweep: deletes batch after batch until one comes back short. Returns the total.</summary>
    private async Task<long> SweepAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var provider = services.GetRequiredService<IClientReadingProvider>();
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
}

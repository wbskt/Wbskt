using Microsoft.Extensions.Options;
using Wbskt.Infrastructure.Billing;
using Wbskt.Infrastructure.Hosting;
using Wbskt.Management.Host.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Models.Billing;

namespace Wbskt.Management.Host.Services.Readings;

/// <summary>
/// Keeps readings for as long as each client's plan says, on <see cref="ReadingsOptions.RetentionInterval"/>:
/// raw readings past the plan's <see cref="PlanLimits.RawReadingsDays"/> are rolled up per hour, hourly
/// rows past its <see cref="PlanLimits.HourlyReadingsDays"/> are deleted, and a deleted client's readings
/// are cleared (<c>dbo.ClientReadings_Compact</c>), in batches until one comes back short.
/// </summary>
public sealed class ClientReadingRetentionService : PeriodicWorker
{
    internal const int BatchSize = 5000;

    private readonly TimeProvider _timeProvider;
    private readonly PlanCatalog _catalog;

    public ClientReadingRetentionService(
        IServiceScopeFactory scopeFactory,
        IOptions<ReadingsOptions> options,
        PlanCatalog catalog,
        ILogger<ClientReadingRetentionService> logger)
        : this(scopeFactory, TimeProvider.System, options, catalog, logger)
    {
    }

    internal ClientReadingRetentionService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<ReadingsOptions> options,
        PlanCatalog catalog,
        ILogger<ClientReadingRetentionService> logger)
        : base(AlwaysOpenWorkGate.Instance, scopeFactory, logger, timeProvider)
    {
        Interval = options.Value.RetentionInterval;
        _timeProvider = timeProvider;
        _catalog = catalog;
    }

    protected override string Name => "Readings retention";

    // A failed sweep is retried on the next tick; it only lets the table grow for an interval longer.
    protected override TimeSpan Interval { get; }

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        await SweepAsync(services, ct);
    }

    /// <summary>One sweep in a scope of its own; see <see cref="SweepAsync(IServiceProvider, CancellationToken)"/>.</summary>
    internal async Task<ReadingCompaction> SweepAsync(CancellationToken cancellationToken)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        return await SweepAsync(scope.ServiceProvider, cancellationToken);
    }

    /// <summary>One sweep: compacts batch after batch until none comes back full. Returns the totals.</summary>
    private async Task<ReadingCompaction> SweepAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var provider = services.GetRequiredService<IClientReadingProvider>();
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        IReadOnlyCollection<ReadingRetention> retention = Enum.GetValues<PlanCode>()
            .Select(plan => (Plan: plan, Limits: _catalog.LimitsFor(plan)))
            .Select(p => new ReadingRetention(p.Plan, p.Limits.RawReadingsDays, p.Limits.HourlyReadingsDays))
            .ToList();

        var total = new ReadingCompaction(0, 0, 0);
        while (true)
        {
            ReadingCompaction batch = await provider.CompactAsync(nowUtc, retention, BatchSize, cancellationToken);
            total = new ReadingCompaction(total.RolledUp + batch.RolledUp, total.HourlyDeleted + batch.HourlyDeleted, total.Cleared + batch.Cleared);
            if (batch.RolledUp < BatchSize && batch.HourlyDeleted < BatchSize && batch.Cleared < BatchSize)
            {
                break;
            }
        }

        if (total.Total > 0)
        {
            Logger.LogInformation(
                "Readings retention rolled {RolledUp} readings up into hours, deleted {HourlyDeleted} hourly rows and cleared {Cleared} rows of deleted clients.",
                total.RolledUp, total.HourlyDeleted, total.Cleared);
        }

        return total;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wbskt.Infrastructure.Hosting;

namespace Wbskt.Infrastructure.StaffActions;

/// <summary>
/// Deletes staff audit rows older than <see cref="Years"/> years, once a day. The audit keeps references, a
/// staff member's name and their reason, and nothing else ever removes a row: TR_StaffAudit_AppendOnly
/// refuses every update, and every delete of a row younger than this.
/// </summary>
public sealed class StaffAuditRetention(IServiceScopeFactory scopeFactory, TimeProvider time, ILogger<StaffAuditRetention> logger)
    : PeriodicWorker(AlwaysOpenWorkGate.Instance, scopeFactory, logger, time)
{
    /// <summary>Seven years, as the table's trigger also holds it (DATEADD(YEAR, -7, ...)).</summary>
    public const int Years = 7;

    internal const int BatchSize = 1000;

    protected override string Name => "Staff audit retention";

    protected override TimeSpan Interval { get; } = TimeSpan.FromDays(1);

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var audit = services.GetRequiredService<IStaffAuditProvider>();

        // A day short of the trigger's cutoff: the trigger reads SQL Server's clock, and a host clock a little
        // ahead of it would otherwise ask for a row the trigger still keeps, refusing the whole batch.
        var before = time.GetUtcNow().UtcDateTime.AddYears(-Years).AddDays(-1);
        long total = 0;
        int deleted;
        do
        {
            deleted = await audit.DeleteExpiredAsync(before, BatchSize, ct);
            total += deleted;
        }
        while (deleted == BatchSize);

        if (total > 0)
        {
            Logger.LogInformation("Staff audit retention deleted {Count} rows written before {BeforeUtc:o}.", total, before);
        }
    }
}

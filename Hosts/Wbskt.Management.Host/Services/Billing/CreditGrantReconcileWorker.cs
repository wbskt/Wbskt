using Microsoft.Extensions.Options;
using Wbskt.Infrastructure.Hosting;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Services.Billing;

/// <summary>
/// Runs <see cref="CreditGrantReconcile"/> every <see cref="BillingOptions.CreditGrantReconcileInterval"/>,
/// so a credit grant whose event was lost is right within the hour. Each management replica runs it; the
/// writes are idempotent, so two at once only repeat work.
/// </summary>
public sealed class CreditGrantReconcileWorker(IServiceScopeFactory scopeFactory, IOptions<BillingOptions> options, ILogger<CreditGrantReconcileWorker> logger)
    : PeriodicWorker(AlwaysOpenWorkGate.Instance, scopeFactory, logger)
{
    protected override string Name => "Credit grant reconcile";

    protected override TimeSpan Interval { get; } = options.Value.CreditGrantReconcileInterval;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        await services.GetRequiredService<CreditGrantReconcile>().ReconcileAsync(ct);
    }
}

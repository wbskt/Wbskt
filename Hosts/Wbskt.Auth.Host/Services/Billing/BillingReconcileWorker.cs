using Wbskt.Infrastructure.Hosting;

namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>
/// Runs <see cref="BillingReconcile"/> once a day, starting a day after the host starts, so a deploy does
/// not read the whole window again. Each auth replica runs it; the sync is safe to repeat.
/// </summary>
public sealed class BillingReconcileWorker(IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<BillingReconcileWorker> logger)
    : PeriodicWorker(AlwaysOpenWorkGate.Instance, scopeFactory, logger, clock)
{
    protected override string Name => "Billing reconcile";

    protected override TimeSpan Interval { get; } = TimeSpan.FromDays(1);

    protected override bool RunOnStart => false;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        await services.GetRequiredService<BillingReconcile>().RunAsync(ct);
    }
}

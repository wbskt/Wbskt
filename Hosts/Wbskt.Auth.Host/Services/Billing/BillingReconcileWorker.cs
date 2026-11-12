using Wbskt.Infrastructure.Hosting;

namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>
/// Asks every hour, from the host's start, whether <see cref="BillingReconcile"/> is due. It runs once every
/// <see cref="BillingOptions.ReconcileEvery"/> across every replica and restart, since the last start is
/// kept in the database: a timer of a day's length would never fire on a host deployed more often.
/// </summary>
public sealed class BillingReconcileWorker(IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<BillingReconcileWorker> logger)
    : PeriodicWorker(AlwaysOpenWorkGate.Instance, scopeFactory, logger, clock)
{
    protected override string Name => "Billing reconcile";

    protected override TimeSpan Interval { get; } = TimeSpan.FromHours(1);

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        await services.GetRequiredService<BillingReconcile>().RunAsync(ct);
    }
}

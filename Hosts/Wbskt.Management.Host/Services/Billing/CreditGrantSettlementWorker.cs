using Wbskt.Infrastructure.Hosting;

namespace Wbskt.Management.Host.Services.Billing;

/// <summary>Runs <see cref="CreditGrantSettlement"/> every hour; a month already settled is skipped.</summary>
public sealed class CreditGrantSettlementWorker(IServiceScopeFactory scopeFactory, ILogger<CreditGrantSettlementWorker> logger)
    : PeriodicWorker(AlwaysOpenWorkGate.Instance, scopeFactory, logger)
{
    protected override string Name => "Credit grant settlement";

    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        await services.GetRequiredService<CreditGrantSettlement>().SettleLastMonthAsync(ct);
    }
}

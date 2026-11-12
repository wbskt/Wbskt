using Microsoft.Extensions.Options;
using Wbskt.Infrastructure.Hosting;
using Wbskt.Management.Host.Models;

namespace Wbskt.Management.Host.Services.Billing;

/// <summary>
/// Runs <see cref="IBusinessOverageService.TrackAsync"/> every <see cref="BillingOptions.BusinessUsageInterval"/>:
/// records the tenants on Business against the month, and closes last month once it is due. Each
/// management replica runs it; both writes take the month's lock and closing is done once.
/// </summary>
public sealed class BusinessUsageWorker(IServiceScopeFactory scopeFactory, IOptions<BillingOptions> options, ILogger<BusinessUsageWorker> logger)
    : PeriodicWorker(AlwaysOpenWorkGate.Instance, scopeFactory, logger)
{
    protected override string Name => "Business usage";

    protected override TimeSpan Interval { get; } = options.Value.BusinessUsageInterval;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        await services.GetRequiredService<IBusinessOverageService>().TrackAsync(ct);
    }
}

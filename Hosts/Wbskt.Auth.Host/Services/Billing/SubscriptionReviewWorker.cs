using Microsoft.Extensions.Options;
using Wbskt.Auth.Host.Providers;
using Wbskt.Infrastructure.Hosting;

namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>
/// Hourly, moves tenants whose plan should change with no webhook to say so: a subscription past due
/// beyond the grace period, or cancelled and past the period paid for, drops to Free; an operator's plan
/// past its end date goes back to the subscription's plan, or to Free. Each subscription is read again
/// from the provider first, so one that recovered keeps its plan.
/// </summary>
public sealed class SubscriptionReviewWorker(IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<SubscriptionReviewWorker> logger)
    : PeriodicWorker(AlwaysOpenWorkGate.Instance, scopeFactory, logger, clock)
{
    private readonly TimeProvider _clock = clock;

    protected override string Name => "Subscription review";

    protected override TimeSpan Interval { get; } = TimeSpan.FromHours(1);

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var billing = services.GetRequiredService<ITenantBillingProvider>();
        var sync = services.GetRequiredService<TenantBillingSync>();
        DateTime now = _clock.GetUtcNow().UtcDateTime;
        TimeSpan grace = services.GetRequiredService<IOptions<BillingOptions>>().Value.PastDueGrace;

        foreach (int tenantId in await billing.GetSubscriptionsToReviewAsync(now, now - grace, ct))
        {
            try
            {
                await sync.ReviewAsync(tenantId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One tenant's provider error must not hold up the rest; it is listed again next hour.
                Logger.LogError(ex, "Could not review tenant {TenantId}'s plan.", tenantId);
            }
        }
    }
}

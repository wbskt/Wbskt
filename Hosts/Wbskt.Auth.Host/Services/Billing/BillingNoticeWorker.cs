using Microsoft.Extensions.Options;
using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Infrastructure.Hosting;

namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>
/// Hourly, tells billing admins what no event announces because it is only the clock moving: a past-due
/// subscription whose grace period ends within <see cref="GraceWarning"/>, and a credit pack that expires
/// within <see cref="PackWarning"/>. <see cref="IBillingNotifier"/> sends each once, so every tick may list
/// the same ones again.
/// </summary>
public sealed class BillingNoticeWorker(IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<BillingNoticeWorker> logger)
    : PeriodicWorker(AlwaysOpenWorkGate.Instance, scopeFactory, logger, clock)
{
    internal static readonly TimeSpan GraceWarning = TimeSpan.FromDays(2);

    internal static readonly TimeSpan PackWarning = TimeSpan.FromDays(30);

    private readonly TimeProvider _clock = clock;

    protected override string Name => "Billing notices";

    protected override TimeSpan Interval { get; } = TimeSpan.FromHours(1);

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken ct)
    {
        var billing = services.GetRequiredService<ITenantBillingProvider>();
        var notifier = services.GetRequiredService<IBillingNotifier>();
        TimeSpan grace = services.GetRequiredService<IOptions<BillingOptions>>().Value.PastDueGrace;
        int retentionGraceDays = services.GetRequiredService<IConfiguration>().GetValue("Billing:RetentionGraceDays", 30);
        DateTime now = _clock.GetUtcNow().UtcDateTime;

        // Fell behind long enough ago that the grace ends within the warning, but not so long that it has ended.
        foreach (PastDueSubscription due in await billing.GetPastDueBetweenAsync(now - grace, now - grace + GraceWarning, ct))
        {
            await NotifyAsync(notifier, due.TenantId, BillingNotices.GraceEnding(due.SubscriptionId, due.PlanCode, due.PastDueSince, grace, retentionGraceDays), ct);
        }

        foreach (ExpiringCreditPurchase pack in await billing.GetCreditPurchasesExpiringAsync(now, now + PackWarning, ct))
        {
            await NotifyAsync(notifier, pack.TenantId, BillingNotices.PackExpiring(pack.PaymentId, pack.Credits, pack.ExpiresAt), ct);
        }
    }

    // One tenant's failure must not keep the rest from hearing; it is listed again next hour.
    private async Task NotifyAsync(IBillingNotifier notifier, int tenantId, BillingNotice notice, CancellationToken ct)
    {
        try
        {
            await notifier.NotifyAsync(tenantId, notice, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError(ex, "Could not tell tenant {TenantId} about {Notice}.", tenantId, notice.Kind);
        }
    }
}

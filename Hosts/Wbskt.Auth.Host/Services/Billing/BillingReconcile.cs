using Microsoft.Extensions.Options;
using Wbskt.Auth.Host.Providers;
using Wbskt.Auth.Host.Telemetry;

namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>
/// Reads every subscription and payment the provider made in the last
/// <see cref="BillingOptions.ReconcileWindow"/> again, through the same sync a webhook starts. The
/// provider stops redelivering a webhook after about a day; one that never got through (the host or its
/// database down, the route wrong, the signing secret rotated on one side) would otherwise leave a tenant
/// that paid on Free, a credit pack never granted, or a cancelled plan kept. The sync is safe to repeat:
/// what was already handled grants, changes and sends nothing again. It runs once every
/// <see cref="BillingOptions.ReconcileEvery"/> across the replicas (<c>dbo.BillingReconcile_Claim</c>).
/// </summary>
internal sealed class BillingReconcile(
    IPaymentGateway gateway,
    TenantBillingSync sync,
    ITenantBillingProvider billing,
    IOptions<BillingOptions> options,
    TimeProvider clock,
    AuthMetrics metrics,
    ILogger<BillingReconcile> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        if (!gateway.IsConfigured)
        {
            return;
        }

        DateTimeOffset now = clock.GetUtcNow();
        if (!await billing.ClaimReconcileAsync(gateway.Name, now.UtcDateTime, options.Value.ReconcileEvery, ct))
        {
            return;
        }

        DateTimeOffset from = now - options.Value.ReconcileWindow;

        // Subscriptions first, so a payment read after its subscription finds it stored.
        await ReconcileAsync("subscription", () => gateway.ListSubscriptionIdsAsync(from, ct), id => sync.SyncSubscriptionAsync(id, ct), ct);
        await ReconcileAsync("payment", () => gateway.ListPaymentIdsAsync(from, ct), id => sync.SyncPaymentAsync(id, ct), ct);
    }

    private async Task ReconcileAsync(string kind, Func<Task<ProviderIdList>> list, Func<string, Task<BillingSyncResult>> read, CancellationToken ct)
    {
        ProviderIdList listed;
        try
        {
            listed = await list();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A list the provider refused (a rotated key, a rate limit, an outage) leaves this kind for the
            // next run; the other kind still goes ahead.
            metrics.RecordBillingReconcile($"{kind}_list", "failed");
            logger.LogError(ex, "Could not list {Provider} {Kind}s to reconcile.", gateway.Name, kind);
            return;
        }

        if (!listed.Complete)
        {
            metrics.RecordBillingReconcile($"{kind}_list", "truncated");
        }

        foreach (string id in listed.Ids)
        {
            await SyncOneAsync(kind, id, () => read(id));
            await Task.Delay(options.Value.ReconcilePause, clock, ct);
        }
    }

    private async Task SyncOneAsync(string kind, string id, Func<Task<BillingSyncResult>> read)
    {
        try
        {
            BillingSyncResult result = await read();
            metrics.RecordBillingReconcile(kind, result.Reason is null ? "done" : "set_aside");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One the provider or the database refused is read again by the next reconcile, while it is
            // still in the window; the rest go on.
            metrics.RecordBillingReconcile(kind, "failed");
            logger.LogError(ex, "Could not reconcile {Provider} {Kind} {Id}.", gateway.Name, kind, id);
        }
    }
}

using Microsoft.Extensions.Options;
using Wbskt.Auth.Host.Telemetry;

namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>
/// Reads every subscription and payment the provider made in the last
/// <see cref="BillingOptions.ReconcileWindow"/> again, through the same sync a webhook starts. The
/// provider stops redelivering a webhook after about a day; one that never got through (the host or its
/// database down, the route wrong, the signing secret rotated on one side) would otherwise leave a tenant
/// that paid on Free, a credit pack never granted, or a cancelled plan kept. The sync is safe to repeat,
/// so reading what was already handled changes nothing.
/// </summary>
internal sealed class BillingReconcile(
    IPaymentGateway gateway,
    TenantBillingSync sync,
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

        DateTimeOffset from = clock.GetUtcNow() - options.Value.ReconcileWindow;

        // Subscriptions first, so a payment read after its subscription finds it stored.
        foreach (string subscriptionId in await gateway.ListSubscriptionIdsAsync(from, ct))
        {
            await SyncOneAsync("subscription", subscriptionId, () => sync.SyncSubscriptionAsync(subscriptionId, ct));
        }

        foreach (string paymentId in await gateway.ListPaymentIdsAsync(from, ct))
        {
            await SyncOneAsync("payment", paymentId, () => sync.SyncPaymentAsync(paymentId, ct));
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

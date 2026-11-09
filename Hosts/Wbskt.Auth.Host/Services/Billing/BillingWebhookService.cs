using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Host.Providers;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Services.Billing;

internal sealed class BillingWebhookService(
    IPaymentGateway gateway,
    ITenantBillingProvider billing,
    TenantBillingSync sync,
    ILogger<BillingWebhookService> logger) : IBillingWebhookService
{
    public async Task<Result> ReceiveAsync(string provider, IReadOnlyDictionary<string, string> headers, string body, CancellationToken ct)
    {
        if (!string.Equals(provider, gateway.Name, StringComparison.OrdinalIgnoreCase) || !gateway.IsConfigured)
        {
            return Result.Failure(Error.NotFound("BILLING_PROVIDER_NOT_FOUND", "No payment provider by that name is set up."));
        }

        if (gateway.ReadWebhook(headers, body) is not { } webhook)
        {
            return Result.Failure(Error.Unauthorized("BILLING_WEBHOOK_SIGNATURE_INVALID", "The webhook signature does not hold up."));
        }

        BillingWebhookDelivery delivery = await billing.RecordWebhookAsync(gateway.Name, webhook.EventId, webhook.Type, body, ct);
        if (delivery.ProcessedAt is not null)
        {
            return Result.Success();
        }

        BillingSyncResult result;
        try
        {
            result = webhook switch
            {
                { PaymentId: { } paymentId } => await sync.SyncPaymentAsync(paymentId, ct),
                { SubscriptionId: { } subscriptionId } => await sync.SyncSubscriptionAsync(subscriptionId, refundedThrough: null, ct),
                _ => BillingSyncResult.SetAside($"A {webhook.Type} event names no subscription or payment.")
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Kept unprocessed with the reason, and failed back to the provider so it delivers the event again.
            await billing.CompleteWebhookAsync(delivery.Id, processed: false, $"{ex.GetType().Name}: {ex.Message}", CancellationToken.None);
            throw;
        }

        if (result.Reason is not null)
        {
            logger.LogWarning("Set aside {Provider} webhook {EventId} ({Type}): {Reason}", gateway.Name, webhook.EventId, webhook.Type, result.Reason);
        }

        await billing.CompleteWebhookAsync(delivery.Id, processed: true, result.Reason, ct);
        return Result.Success();
    }
}

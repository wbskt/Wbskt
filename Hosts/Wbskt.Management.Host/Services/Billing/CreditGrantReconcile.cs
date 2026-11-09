using Wbskt.Auth.Models;
using Wbskt.Management.Host.Providers;
using Wbskt.Management.Host.Services.Clients;
using Wbskt.Management.Host.Telemetry;
using Wbskt.Models;

namespace Wbskt.Management.Host.Services.Billing;

/// <summary>
/// The backstop behind <c>TenantCreditsGrantedEvent</c>, <c>TenantCreditGrantRevokedEvent</c> and
/// <c>TenantCreditGrantHoldChangedEvent</c>: reads every credit pack bought from the auth host, which owns
/// them, and writes what the main database's grant is missing. Each write is the one the event would
/// have made, and each is idempotent (a grant once per source and reference, a revocation once, a hold
/// only at a newer revision), so it can run beside the events in any order. Every write that changed
/// something was a lost event, and counts as a repair.
/// </summary>
public sealed class CreditGrantReconcile(
    ICreditPurchasesClient auth,
    ICreditGrantProvider grants,
    ManagementMetrics metrics,
    TimeProvider clock,
    ILogger<CreditGrantReconcile> logger)
{
    /// <summary>Returns how many grants were repaired.</summary>
    public async Task<int> ReconcileAsync(CancellationToken ct)
    {
        int repaired = 0;
        string? cursor = null;
        do
        {
            Page<CreditPurchaseResponse> page = await auth.GetPageAsync(cursor, ct);
            foreach (CreditPurchaseResponse purchase in page.Items)
            {
                repaired += await ReconcileAsync(purchase, ct);
            }

            cursor = page.NextCursor;
        }
        while (cursor is not null);

        if (repaired > 0)
        {
            logger.LogWarning("Credit grant reconcile repaired {Repaired} grant change(s) an event did not deliver.", repaired);
        }

        return repaired;
    }

    private async Task<int> ReconcileAsync(CreditPurchaseResponse purchase, CancellationToken ct)
    {
        int repaired = 0;
        if (await grants.InsertAsync(purchase.TenantId, purchase.Credits, purchase.PurchasedAt.UtcDateTime, purchase.ExpiresAt.UtcDateTime, purchase.Source, purchase.SourceRef, ct))
        {
            repaired += Repaired(purchase, "granted");
        }

        if (purchase.RefundedAt is { } refundedAt && await grants.RevokeAsync(purchase.Source, purchase.SourceRef, refundedAt.UtcDateTime, ct))
        {
            repaired += Repaired(purchase, "revoked");
        }

        // Revision 0 was never held; a newer revision than the grant has is a hold or release it missed.
        if (purchase.HoldRevision > 0
            && await grants.SetHoldAsync(purchase.Source, purchase.SourceRef, purchase.Held, purchase.HoldRevision, clock.GetUtcNow().UtcDateTime, ct))
        {
            repaired += Repaired(purchase, purchase.Held ? "held" : "released");
        }

        return repaired;
    }

    private int Repaired(CreditPurchaseResponse purchase, string change)
    {
        logger.LogWarning("Repaired the credit grant {Source} {SourceRef} of tenant {TenantId}: {Change}.", purchase.Source, purchase.SourceRef, purchase.TenantId, change);
        metrics.RecordCreditGrantRepaired(change);
        return 1;
    }
}

using Wbskt.Auth.Host.Models;

namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>Emails a tenant's billing admins about its plan, credits, messages and payments.</summary>
internal interface IBillingNotifier
{
    /// <summary>
    /// Sends <paramref name="notice"/> to each member of the tenant who manages billing, unless it was
    /// sent before for its kind and period. Nothing is claimed while there is no relay or no one to write to.
    /// </summary>
    Task NotifyAsync(int tenantId, BillingNotice notice, CancellationToken cancellationToken);
}

using Wbskt.Models.Billing;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// What a tenant's plan allows, for the limits the auth host holds itself (workspaces and members). Read
/// from <c>dbo.Tenants</c>, which this host owns, so it is never behind a plan change.
/// </summary>
internal interface ITenantLimits
{
    Task<PlanLimits> ForTenantAsync(int tenantId, CancellationToken cancellationToken);

    Task<PlanLimits> ForTenantAsync(Guid tenantRef, CancellationToken cancellationToken);
}

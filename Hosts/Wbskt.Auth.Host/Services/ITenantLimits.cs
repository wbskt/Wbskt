using Wbskt.Infrastructure;
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

    /// <summary>
    /// <paramref name="refusal"/> (<c>PLAN_WORKSPACE_LIMIT_REACHED</c> or <c>PLAN_MEMBER_LIMIT_REACHED</c>)
    /// with the tenant's plan, its limit and the seats it holds now; any other error comes back as it is.
    /// </summary>
    Task<Error> WithSeatsAsync(Error refusal, Guid tenantRef, CancellationToken cancellationToken);

    /// <inheritdoc cref="WithSeatsAsync(Error, Guid, CancellationToken)"/>
    Task<Error> WithSeatsAsync(Error refusal, int tenantId, CancellationToken cancellationToken);
}

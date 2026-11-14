namespace Wbskt.Auth.Host.Services;

/// <summary>Asks billing to put a tenant on the plan its subscription decides now, as the hourly review would.</summary>
internal interface ITenantPlanReview
{
    Task ReviewAsync(int tenantId, CancellationToken cancellationToken);
}

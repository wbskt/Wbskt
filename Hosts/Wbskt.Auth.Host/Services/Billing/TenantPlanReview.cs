namespace Wbskt.Auth.Host.Services.Billing;

/// <summary>The billing review of one tenant, as <see cref="SubscriptionReviewWorker"/> runs it every hour.</summary>
internal sealed class TenantPlanReview(TenantBillingSync sync) : ITenantPlanReview
{
    public Task ReviewAsync(int tenantId, CancellationToken cancellationToken) => sync.ReviewAsync(tenantId, cancellationToken);
}

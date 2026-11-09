using Wbskt.Auth.Host.Services;
using Wbskt.Infrastructure.Billing;
using Wbskt.Models.Billing;

namespace Wbskt.Host.Tests;

/// <summary>Every tenant on one plan's limits, for auth tests that are not about how the plan is read.</summary>
internal sealed class FixedTenantLimits(PlanLimits? limits = null) : ITenantLimits
{
    public PlanLimits Limits { get; } = limits ?? PlanCatalog.BuiltIn[PlanCode.Free];

    public Task<PlanLimits> ForTenantAsync(int tenantId, CancellationToken cancellationToken) => Task.FromResult(Limits);

    public Task<PlanLimits> ForTenantAsync(Guid tenantRef, CancellationToken cancellationToken) => Task.FromResult(Limits);
}

using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Models.Admin;

namespace Wbskt.Auth.Host.Services;

/// <summary>What the platform admin host reads and changes about a tenant, through the services that own each rule.</summary>
public interface IAdminTenantService
{
    Task<Result<AdminTenantLimitsResponse>> GetLimitsAsync(Guid tenantRef, CancellationToken cancellationToken);

    Task<Result<StaffActionResponse>> SetPlanAsync(Guid tenantRef, AdminSetTenantPlanRequest request, CancellationToken cancellationToken);

    Task<Result<StaffActionResponse>> SetLimitsAsync(Guid tenantRef, AdminSetTenantLimitsRequest request, CancellationToken cancellationToken);
}

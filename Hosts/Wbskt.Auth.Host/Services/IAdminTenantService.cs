using Wbskt.Auth.Models;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Services;

/// <summary>What the platform admin host reads and changes about a tenant, through the services that own each rule.</summary>
public interface IAdminTenantService
{
    Task<Result<AdminTenantLimitsResponse>> GetLimitsAsync(Guid tenantRef, CancellationToken cancellationToken);
}

using Wbskt.Auth.Models;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Services;

public interface ITenantUsageService
{
    /// <summary>The tenant's plan, its limits, and its workspaces and members against them.</summary>
    Task<Result<TenantUsageResponse>> GetAsync(int tenantId, CancellationToken cancellationToken);
}

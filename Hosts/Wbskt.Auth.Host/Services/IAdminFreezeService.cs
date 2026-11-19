using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Models.Admin;

namespace Wbskt.Auth.Host.Services;

/// <summary>Freezes a tenant for a staff member, or lifts the freeze, with its staff audit row.</summary>
public interface IAdminFreezeService
{
    Task<Result<StaffActionResponse>> SetFrozenAsync(Guid tenantRef, AdminSetTenantFrozenRequest request, CancellationToken cancellationToken);
}

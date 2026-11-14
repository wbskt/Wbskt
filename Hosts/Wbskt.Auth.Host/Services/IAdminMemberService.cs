using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Models.Admin;

namespace Wbskt.Auth.Host.Services;

/// <summary>Changes the platform admin host makes to a tenant's members for a staff member, each with its staff audit row.</summary>
public interface IAdminMemberService
{
    Task<Result<StaffActionResponse>> SetSuspendedAsync(Guid tenantRef, Guid userRef, AdminSetMemberSuspendedRequest request, CancellationToken cancellationToken);

    Task<Result<StaffActionResponse>> RemoveAsync(Guid tenantRef, Guid userRef, AdminRemoveMemberRequest request, CancellationToken cancellationToken);
}

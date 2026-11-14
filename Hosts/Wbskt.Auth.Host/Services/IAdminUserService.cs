using Wbskt.Auth.Models;
using Wbskt.Infrastructure;
using Wbskt.Models.Admin;

namespace Wbskt.Auth.Host.Services;

/// <summary>Changes the platform admin host makes to a person's account for a staff member, each with its staff audit row.</summary>
public interface IAdminUserService
{
    Task<Result<StaffActionResponse>> ClearLockoutAsync(Guid userRef, AdminUserActionRequest request, CancellationToken cancellationToken);

    Task<Result<StaffActionResponse>> ResendVerificationAsync(Guid userRef, AdminUserActionRequest request, CancellationToken cancellationToken);

    Task<Result<StaffActionResponse>> SendPasswordResetAsync(Guid userRef, AdminUserActionRequest request, CancellationToken cancellationToken);

    Task<Result<StaffActionResponse>> SignOutEverywhereAsync(Guid userRef, AdminUserActionRequest request, CancellationToken cancellationToken);

    Task<Result<StaffActionResponse>> SetSignInAsync(Guid userRef, AdminSetUserSignInRequest request, CancellationToken cancellationToken);
}

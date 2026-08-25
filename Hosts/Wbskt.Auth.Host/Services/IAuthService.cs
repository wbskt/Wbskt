using Wbskt.Auth.Host.Models;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Authentication only: establishing, refreshing and ending sessions. Tenant administration lives
/// in <see cref="IManagementService"/>.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Creates an account and the tenant it owns. A non-null <paramref name="invitationToken"/>
    /// additionally joins the new account to the tenant that issued it; the account gets its own
    /// tenant either way.
    /// </summary>
    Task<Result> RegisterUserAsync(string username, string email, string password, string? invitationToken = null, CancellationToken cancellationToken = default);
    Task<Result<LoginResponse>> LoginAsync(string email, string password, string ipAddress, CancellationToken cancellationToken = default);
    Task<Result<LoginResponse>> RefreshTokenAsync(string token, string ipAddress, CancellationToken cancellationToken = default);
    Task<Result> LogoutAsync(string token, string ipAddress, CancellationToken cancellationToken = default);
    Task<Result> LogoutAllAsync(int userId, string ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues a password-reset link if the address has a usable account. Succeeds either way — whether
    /// an address is registered is precisely what this endpoint must not disclose.
    /// </summary>
    Task<Result> ForgotPasswordAsync(string email, string ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redeems a reset token, writes the new password, and revokes every refresh token the account
    /// has — so a session an attacker already holds does not survive the recovery.
    /// </summary>
    Task<Result> ResetPasswordAsync(string token, string newPassword, string ipAddress, CancellationToken cancellationToken = default);

    /// <summary>Redeems a verification token and marks the account's address confirmed.</summary>
    Task<Result> VerifyEmailAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reissues a verification link. Takes an address rather than a caller, because sign-in requires
    /// a verified address: an account that needs this cannot authenticate to ask for it. Succeeds
    /// whether or not the address has an unverified account, for the same reason as
    /// <see cref="ForgotPasswordAsync"/>.
    /// </summary>
    Task<Result> ResendVerificationAsync(string email, CancellationToken cancellationToken = default);
}

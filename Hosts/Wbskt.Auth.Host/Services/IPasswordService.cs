using Wbskt.Auth.Models;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Services;

/// <summary>Changing a password while signed in, and recovering an account whose password is lost.</summary>
public interface IPasswordService
{
    /// <summary>
    /// Changes a signed-in user's password after checking the current one, ends every session the
    /// account has, and returns a fresh session for the caller.
    /// </summary>
    Task<Result<LoginResponse>> ChangePasswordAsync(int userId, string currentPassword, string newPassword, string ipAddress, CancellationToken cancellationToken = default);

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
}

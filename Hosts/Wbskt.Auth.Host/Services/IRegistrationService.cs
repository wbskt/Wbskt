using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Services;

/// <summary>Creating an account, and confirming the address it was created with.</summary>
public interface IRegistrationService
{
    /// <summary>
    /// Creates an account and the tenant it owns. A non-null <paramref name="invitationToken"/>
    /// additionally joins the new account to the tenant that issued it; the account gets its own
    /// tenant either way.
    /// </summary>
    Task<Result> RegisterUserAsync(string username, string email, string password, string? invitationToken = null, CancellationToken cancellationToken = default);

    /// <summary>Redeems a verification token and marks the account's address confirmed.</summary>
    Task<Result> VerifyEmailAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reissues a verification link. Takes an address rather than a caller, because sign-in requires
    /// a verified address: an account that needs this cannot authenticate to ask for it. Succeeds
    /// whether or not the address has an unverified account, for the same reason as
    /// <see cref="IPasswordService.ForgotPasswordAsync"/>.
    /// </summary>
    Task<Result> ResendVerificationAsync(string email, CancellationToken cancellationToken = default);
}

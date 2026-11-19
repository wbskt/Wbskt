using Wbskt.Auth.Host.Models;
using Wbskt.Auth.Models;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Sessions: signing in, refreshing and signing out, and the list of a user's live sessions. Passwords
/// live in <see cref="IPasswordService"/>, new accounts in <see cref="IRegistrationService"/>.
/// </summary>
public interface ISignInService
{
    Task<Result<LoginResponse>> LoginAsync(string email, string password, string ipAddress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs in from a browser that keeps a device cookie. While the account is locked, the password
    /// is still tried when <paramref name="deviceToken"/> is one this account was given at an earlier
    /// sign-in, so a stranger's wrong guesses cannot keep the owner out. A success hands out a new
    /// device token for the cookie.
    /// </summary>
    Task<Result<BrowserSignIn>> LoginFromBrowserAsync(string email, string password, string ipAddress, string? deviceToken, CancellationToken cancellationToken = default);
    Task<Result<LoginResponse>> RefreshTokenAsync(string token, string ipAddress, CancellationToken cancellationToken = default);
    Task<Result> LogoutAsync(string token, string ipAddress, CancellationToken cancellationToken = default);
    Task<Result> LogoutAllAsync(int userId, string ipAddress, CancellationToken cancellationToken = default);

    /// <summary>The user's live sessions, newest first, with <paramref name="currentSessionId"/> marked as current.</summary>
    Task<Result<IReadOnlyCollection<SessionResponse>>> GetSessionsAsync(int userId, Guid? currentSessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends one of the user's own sessions, its access token included. NotFound when it is not a live
    /// session of theirs.
    /// </summary>
    Task<Result> RevokeSessionAsync(int userId, Guid sessionId, string ipAddress, CancellationToken cancellationToken = default);
}

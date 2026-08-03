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
}

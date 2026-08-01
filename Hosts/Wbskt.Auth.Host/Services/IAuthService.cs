using Wbskt.Auth.Host.Models;
using Wbskt.Infrastructure;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Authentication only: establishing, refreshing and ending sessions. Tenant administration lives
/// in <see cref="IManagementService"/>.
/// </summary>
public interface IAuthService
{
    Task<Result> RegisterUserAsync(string username, string email, string password, CancellationToken cancellationToken = default);
    Task<Result<LoginResponse>> LoginAsync(string email, string password, string ipAddress, CancellationToken cancellationToken = default);
    Task<Result<LoginResponse>> RefreshTokenAsync(string token, string ipAddress, CancellationToken cancellationToken = default);
    Task<Result> LogoutAsync(string token, string ipAddress, CancellationToken cancellationToken = default);
    Task<Result> LogoutAllAsync(int userId, string ipAddress, CancellationToken cancellationToken = default);
}

using Wbskt.Common.Records;
using Wbskt.Identity.Api.Contracts;

namespace Wbskt.Identity.Api.Services;

public interface IAuthService
{
    Task<UserLoginResponse> Login(UserLoginRequest loginRequest, string ipAddress, CancellationToken cancellationToken);
    Task<UserLoginResponse> RotateRefreshToken(string refreshToken, string ipAddress, CancellationToken cancellationToken);
    Task<UserRecord> RegisterUser(UserRegistrationRequest request, CancellationToken cancellationToken);
}

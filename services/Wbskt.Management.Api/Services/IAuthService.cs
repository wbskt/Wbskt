using Wbskt.Common.Records;
using Wbskt.Management.Api.Contracts;

namespace Wbskt.Management.Api.Services;

public interface IAuthService
{
    Task<UserLoginResponse> Login(UserLoginRequest loginRequest, string ipAddress, CancellationToken cancellationToken);
    Task<UserLoginResponse> RotateRefreshToken(string refreshToken, string ipAddress, CancellationToken cancellationToken);
    Task<UserRecord> RegisterUser(UserRegistrationRequest request, CancellationToken cancellationToken);
}

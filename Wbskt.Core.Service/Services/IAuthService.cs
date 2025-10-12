using Wbskt.Common.Records;
using Wbskt.Core.Service.Contracts;

namespace Wbskt.Core.Service.Services;

public interface IAuthService
{
    Task<UserLoginResponse> Login(UserLoginRequest loginRequest, string ipAddress, CancellationToken cancellationToken);
    Task<UserLoginResponse> RotateRefreshToken(string refreshToken, string ipAddress, CancellationToken cancellationToken);
    Task<UserRecord> RegisterUser(UserRegistrationRequest request, CancellationToken cancellationToken);
}

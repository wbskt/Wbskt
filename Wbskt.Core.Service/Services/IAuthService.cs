using Wbskt.Common.Records;

namespace Wbskt.Core.Service.Services;

public interface IAuthService
{
    string GenerateToken(UserRecord userData);

    Task<bool> ValidatePassword(UserLoginRequest loginRequest, CancellationToken cancellationToken);

    Task<UserRecord> RegisterUser(UserRegistrationRequest request, CancellationToken cancellationToken);
}

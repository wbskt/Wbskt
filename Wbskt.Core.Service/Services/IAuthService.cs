using Wbskt.Common.Contracts;

namespace Wbskt.Core.Service.Services;

public interface IAuthService
{
    string GenerateToken(User userData);

    bool ValidatePassword(UserLoginRequest loginRequest);

    User RegisterUser(UserRegistrationRequest request);

    string CreateCoreServerToken();
}

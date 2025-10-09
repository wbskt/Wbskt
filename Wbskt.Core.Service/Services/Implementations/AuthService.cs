using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Wbskt.Common;
using Wbskt.Common.Contracts;
using Wbskt.Common.Exceptions;

namespace Wbskt.Core.Service.Services.Implementations;

internal sealed class AuthService(ILogger<AuthService> logger, IConfiguration configuration, IUsersService usersService, IPasswordHasher<User> passwordHasher) : IAuthService
{
    public string GenerateToken(User userData)
    {
        var tokenHandler = new JsonWebTokenHandler();
        var configurationKey = configuration[Constants.JwtKeyNames.UserTokenKey];

        var key = Encoding.UTF8.GetBytes(configurationKey!);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new Claim[]
            {
                new(Constants.Claims.EmailId, userData.EmailId),
                new(Constants.Claims.Name, userData.Name),
                new(Constants.Claims.UserData, userData.UserId.ToString())
            }),
            Expires = DateTime.UtcNow.AddDays(1),
            Issuer = configuration[Constants.JwtKeyNames.Issuer],
            Audience = configuration[Constants.JwtKeyNames.Audience],
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256)
        };

        return tokenHandler.CreateToken(tokenDescriptor);
    }

    public string CreateCoreServerToken()
    {
        var tokenHandler = new JsonWebTokenHandler();
        var configurationKey = configuration[Constants.JwtKeyNames.CoreServerTokenKey];

        var key = Encoding.UTF8.GetBytes(configurationKey!);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new Claim[]
            {
                new(Constants.Claims.CoreServer, Guid.NewGuid().ToString())
            }),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256),
            Expires = DateTime.Now.AddMinutes(Constants.ExpiryTimes.ServerTokenExpiry),
            Issuer = configuration[Constants.JwtKeyNames.Issuer],
            Audience = configuration[Constants.JwtKeyNames.Audience]
        };

        logger.LogDebug("core server token created");
        return tokenHandler.CreateToken(tokenDescriptor);
    }

    public User RegisterUser(UserRegistrationRequest request)
    {
        if (usersService.FindUserIdByEmailId(request.EmailId) > 0)
        {
            throw WbsktExceptions.EmailIdExists(request.EmailId);
        }

        var hashedPassword = passwordHasher.HashPassword(null!, request.Password);
        var user = new User { EmailId = request.EmailId, PasswordHash = hashedPassword, Name = request.UserName };
        return usersService.AddUser(user);
    }

    public bool ValidatePassword(UserLoginRequest loginRequest)
    {
        var user = usersService.GetUserByEmailId(loginRequest.EmailId);

        var result = passwordHasher.VerifyHashedPassword(null!, user.PasswordHash, loginRequest.Password);

        if (result != PasswordVerificationResult.Success)
        {
            return false;
        }

        return true;
    }
}

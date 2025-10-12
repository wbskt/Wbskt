using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Wbskt.Common;
using Wbskt.Common.Exceptions;
using Wbskt.Common.Readers;
using Wbskt.Common.Records;
using Wbskt.Common.Writers;

namespace Wbskt.Core.Service.Services.Implementations;

internal sealed class AuthService(
    ILogger<AuthService> logger, 
    IConfiguration configuration, 
    IUsersReader usersReader, 
    IUsersWriter usersWriter,
    IPasswordHasher<UserRecord> passwordHasher) : IAuthService
{
    public string GenerateToken(UserRecord userData)
    {
        var tokenHandler = new JsonWebTokenHandler();
        var configurationKey = configuration[Constants.JwtKeyNames.UserTokenKey];

        var key = Encoding.UTF8.GetBytes(configurationKey!);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim(Constants.Claims.EmailId, userData.Email),
                new Claim(Constants.Claims.Name, userData.Name),
                new Claim(Constants.Claims.UserData, userData.Id.ToString())
            ]),
            Expires = DateTime.UtcNow.AddDays(1),
            Issuer = configuration[Constants.JwtKeyNames.Issuer],
            Audience = configuration[Constants.JwtKeyNames.Audience],
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256)
        };

        return tokenHandler.CreateToken(tokenDescriptor);
    }

    public async Task<UserRecord> RegisterUser(UserRegistrationRequest request, CancellationToken cancellationToken)
    {
        var userId = await usersReader.FindByEmailIdAsync(request.EmailId, cancellationToken);
        if (userId > 0)
        {
            throw WbsktExceptions.EmailIdExists(request.EmailId);
        }

        var user = new UserRecord
        {
            Email = request.EmailId, 
            PasswordHash = passwordHasher.HashPassword(null!, request.Password), 
            Name = request.UserName
        };
        
        var newUserId = await usersWriter.InsertAsync(user, cancellationToken);

        return user with { Id = newUserId };
    }

    public async Task<bool> ValidatePassword(UserLoginRequest loginRequest, CancellationToken cancellationToken)
    {
        var user = await usersReader.GetByEmailIdAsync(loginRequest.EmailId, cancellationToken);

        if (user == null) return false;
        
        var result = passwordHasher.VerifyHashedPassword(null!, user.PasswordHash, loginRequest.Password);

        return result == PasswordVerificationResult.Success;
    }
}

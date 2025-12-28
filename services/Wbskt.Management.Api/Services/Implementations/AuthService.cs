using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Wbskt.Common;
using Wbskt.Common.Exceptions;
using Wbskt.Common.Readers;
using Wbskt.Common.Readers.Database;
using Wbskt.Common.Records;
using Wbskt.Common.Writers;
using Wbskt.Management.Api.Contracts;

namespace Wbskt.Management.Api.Services.Implementations;

internal sealed class AuthService : IAuthService
{
    private readonly IConfiguration _configuration;
    private readonly IUsersReader _usersReader;
    private readonly IUsersWriter _usersWriter;
    private readonly IUserRefreshTokensDatabaseReader _refreshTokenReader;
    private readonly IUserRefreshTokensWriter _refreshTokenWriter;
    private readonly IPasswordHasher<UserRecord> _passwordHasher;

    public AuthService(
        IConfiguration configuration,
        IUsersReader usersReader,
        IUsersWriter usersWriter,
        IUserRefreshTokensDatabaseReader refreshTokenReader,
        IUserRefreshTokensWriter refreshTokenWriter,
        IPasswordHasher<UserRecord> passwordHasher)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _usersReader = usersReader ?? throw new ArgumentNullException(nameof(usersReader));
        _usersWriter = usersWriter ?? throw new ArgumentNullException(nameof(usersWriter));
        _refreshTokenReader = refreshTokenReader ?? throw new ArgumentNullException(nameof(refreshTokenReader));
        _refreshTokenWriter = refreshTokenWriter ?? throw new ArgumentNullException(nameof(refreshTokenWriter));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
    }

    public async Task<UserLoginResponse> Login(UserLoginRequest loginRequest, string ipAddress, CancellationToken cancellationToken)
    {
        var user = await _usersReader.GetByEmailIdAsync(loginRequest.EmailId, cancellationToken);

        if (user == null)
        {
            throw WbsktExceptions.UserNotFound(loginRequest.EmailId);
        }

        var result = _passwordHasher.VerifyHashedPassword(null!, user.PasswordHash, loginRequest.Password);

        if (result == PasswordVerificationResult.Failed)
        {
            throw WbsktExceptions.InvalidCredentials();
        }

        var accessToken = GenerateToken(user);
        var refreshToken = await GenerateRefreshToken(user.Id, ipAddress, cancellationToken);

        return new UserLoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token
        };
    }

    public async Task<UserLoginResponse> RotateRefreshToken(string token, string ipAddress, CancellationToken cancellationToken)
    {
        var refreshToken = await _refreshTokenReader.GetByTokenAsync(token, cancellationToken);

        if (refreshToken == null)
        {
            throw WbsktExceptions.InvalidToken();
        }

        if (refreshToken.Revoked.HasValue)
        {
            throw WbsktExceptions.InvalidToken();
        }

        if (refreshToken.Expires < DateTime.UtcNow)
        {
            throw WbsktExceptions.InvalidToken();
        }

        var user = await _usersReader.GetByIdAsync(refreshToken.UserId, cancellationToken);

        if (user == null)
        {
            throw WbsktExceptions.UserNotFound(refreshToken.UserId.ToString());
        }

        var newRefreshToken = await GenerateRefreshToken(user.Id, ipAddress, cancellationToken);

        refreshToken = refreshToken with
        {
            Revoked = DateTime.UtcNow,
            RevokedByIp = ipAddress,
            ReplacedByToken = newRefreshToken.Token
        };

        await _refreshTokenWriter.UpdateAsync(refreshToken, cancellationToken);

        var accessToken = GenerateToken(user);

        return new UserLoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshToken.Token
        };
    }

    public async Task<UserRecord> RegisterUser(UserRegistrationRequest request, CancellationToken cancellationToken)
    {
        var userId = await _usersReader.FindByEmailIdAsync(request.EmailId, cancellationToken);
        if (userId > 0)
        {
            throw WbsktExceptions.EmailIdExists(request.EmailId);
        }

        var user = new UserRecord
        {
            Email = request.EmailId,
            PasswordHash = _passwordHasher.HashPassword(null!, request.Password),
            Name = request.UserName
        };

        var newUserId = await _usersWriter.InsertAsync(user, cancellationToken);

        return user with { Id = newUserId };
    }

    private string GenerateToken(UserRecord userData)
    {
        var tokenHandler = new JsonWebTokenHandler();
        var configurationKey = _configuration[Constants.JwtKeyNames.UserTokenKey];

        var key = Encoding.UTF8.GetBytes(configurationKey!);
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim(Constants.Claims.EmailId, userData.Email),
                new Claim(Constants.Claims.Name, userData.Name),
                new Claim(Constants.Claims.UserData, userData.Id.ToString())
            ]),
            Expires = DateTime.UtcNow.AddMinutes(15),
            Issuer = _configuration[Constants.JwtKeyNames.Issuer],
            Audience = _configuration[Constants.JwtKeyNames.Audience],
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256)
        };

        return tokenHandler.CreateToken(tokenDescriptor);
    }

    private async Task<RefreshTokenRecord> GenerateRefreshToken(int userId, string ipAddress, CancellationToken cancellationToken)
    {
        var refreshToken = new RefreshTokenRecord
        {
            UserId = userId,
            Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            Expires = DateTime.UtcNow.AddDays(7),
            Created = DateTime.UtcNow,
            CreatedByIp = ipAddress
        };

        await _refreshTokenWriter.InsertAsync(refreshToken, cancellationToken);

        return refreshToken;
    }
}

using System.Security.Cryptography;
using Wbskt.Auth.Host.Models;
using Wbskt.Infrastructure.Security;

namespace Wbskt.Auth.Host.Services;

/// <summary>
/// Mints the two halves of a session: the access token, signed for the API, and the random refresh
/// token whose hash the database keeps. Shared by sign-in, which starts sessions, and the password
/// change, which ends every session and starts a fresh one for the caller.
/// </summary>
internal sealed class SessionTokens
{
    private readonly IJwtService _jwtService;
    private readonly TimeSpan _accessTokenLifetime;
    private readonly TimeSpan _refreshTokenLifetime;

    public SessionTokens(IJwtService jwtService, AccessTokenOptions options)
    {
        _jwtService = jwtService;
        _accessTokenLifetime = options.AccessTokenLifetime;
        _refreshTokenLifetime = options.RefreshTokenLifetime;
        SessionLifetime = options.SessionLifetime;
    }

    /// <summary>How long a session lasts from sign-in, however often it is refreshed.</summary>
    public TimeSpan SessionLifetime { get; }

    public string AccessToken(User user, Guid sessionId)
    {
        return _jwtService.GenerateToken(WbsktClaims.ForUser(user.RefId, user.Username, user.Email, sessionId), JwtAudiences.Api, _accessTokenLifetime);
    }

    public RefreshToken NewRefreshToken(int userId)
    {
        using var rng = RandomNumberGenerator.Create();
        var randomBytes = new byte[64];
        rng.GetBytes(randomBytes);

        // A new session id: used as-is when this token starts a sign-in, and ignored by rotation,
        // which keeps the presented token's.
        return new RefreshToken
        {
            UserId = userId,
            SessionId = Guid.NewGuid(),
            Token = Convert.ToBase64String(randomBytes),
            Expires = DateTime.UtcNow.Add(_refreshTokenLifetime),
        };
    }
}
